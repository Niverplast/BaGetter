using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using BaGetter.Core.Email;
using BaGetter.Core.Entities;
using BaGetter.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGetter.Core.Notifications;

/// <summary>
/// Periodically scans personal access tokens and emails their owners as expiry approaches,
/// once per configured threshold (see <see cref="PatExpiryNotificationOptions"/>).
/// </summary>
public class PatExpiryNotificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PatExpiryNotificationOptions _options;
    private readonly IPatExpiryEmailBuilder _emailBuilder;
    private readonly SystemTime _systemTime;
    private readonly ILogger<PatExpiryNotificationService> _logger;

    public PatExpiryNotificationService(
        IServiceProvider serviceProvider,
        IOptions<PatExpiryNotificationOptions> options,
        IPatExpiryEmailBuilder emailBuilder,
        SystemTime systemTime,
        ILogger<PatExpiryNotificationService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _emailBuilder = emailBuilder ?? throw new ArgumentNullException(nameof(emailBuilder));
        _systemTime = systemTime ?? throw new ArgumentNullException(nameof(systemTime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("PAT expiry notifications are disabled; scanner will not run.");
            return;
        }

        // Nothing to do when no email backend is configured: the sender resolves to the no-op
        // NullEmailSender, so scanning would only burn the per-token notification state on
        // messages that are never delivered.
        using (var scope = _serviceProvider.CreateScope())
        {
            if (scope.ServiceProvider.GetRequiredService<IEmailSender>() is NullEmailSender)
            {
                _logger.LogInformation("Email is not configured; PAT expiry scanner will not run.");
                return;
            }
        }

        var interval = TimeSpan.FromHours(_options.ScanIntervalHours);


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a single scan failure kill the loop.
                _logger.LogError(ex, "PAT expiry notification scan failed; will retry after {Interval}.", interval);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs a single scan pass: finds tokens crossing a notification threshold, emails their
    /// owners, and records the threshold so the same warning is not sent twice.
    /// </summary>
    /// <returns>The number of notification emails sent.</returns>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting PAT expiry notification scan.");
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        // Defensive: never load tokens or record notification state when the effective sender is
        // the no-op sender (email not configured). ExecuteAsync already guards this, but keep the
        // invariant local so no-op sends can never advance the per-token de-dup threshold.
        if (emailSender is NullEmailSender)
        {
            _logger.LogInformation("Email is not configured; skipping PAT expiry scan.");
            return 0;
        }

        var now = _systemTime.UtcNow;
        var thresholds = _options.EffectiveNotificationDays;
        var maxThreshold = thresholds.Max();

        // Widen the window by a day so tokens on the boundary are not missed due to time-of-day.
        var cutoff = now.AddDays(maxThreshold + 1);

        // Only consider tokens that have not expired yet: a token past its expiry cannot be
        // "expiring soon", and warning about it would be misleading (and would fire for every
        // historically-expired, never-revoked token the first time the scanner runs).
        var tokens = await context.PersonalAccessTokens
            .Include(t => t.User)
            .Where(t => !t.IsRevoked && t.ExpiresAtUtc >= now && t.ExpiresAtUtc <= cutoff)
            .ToListAsync(cancellationToken);

        var sent = 0;

        foreach (var token in tokens)
        {
            var daysUntil = DaysUntil(token.ExpiresAtUtc, now);
            var due = GetDueThreshold(daysUntil, thresholds);
            if (due is null)
                continue;

            // Only send if we have not already notified at this (or a nearer) threshold.
            if (token.ExpiryNotificationThresholdDays is int already && already <= due.Value)
                continue;

            if (token.User is null || !token.User.IsEnabled)
                continue;

            if (string.IsNullOrWhiteSpace(token.User.Email))
            {
                _logger.LogWarning(
                    "Token {TokenId} for user {UserId} expires soon but the user has no email address; skipping notification.",
                    token.Id, token.UserId);
                continue;
            }

            try
            {
                var message = _emailBuilder.Build(token, daysUntil);
                await emailSender.SendAsync(message, cancellationToken);

                // Record the threshold immediately so a later failure in this scan does not
                // discard progress (which would re-send this warning on the next scan).
                token.ExpiryNotificationThresholdDays = due.Value;
                await context.SaveChangesAsync(cancellationToken);
                sent++;

                _logger.LogInformation(
                    "Sent expiry notification for token {TokenId} (user {UserId}) at the {Threshold}-day threshold.",
                    token.Id, token.UserId, due.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Isolate per-token failures (e.g. a bad recipient address or a transient
                // send error) so one token cannot starve the rest of the batch.
                _logger.LogError(ex,
                    "Failed to send expiry notification for token {TokenId} (user {UserId}); skipping.",
                    token.Id, token.UserId);
            }
        }

        _logger.LogInformation("PAT expiry notification scan complete; {Sent} emails sent.", sent);
        return sent;
    }

    /// <summary>
    /// The whole number of days from <paramref name="now"/> to <paramref name="expiresAtUtc"/>,
    /// measured by calendar date (so the expiry day itself is <c>0</c>, and past dates are negative).
    /// </summary>
    internal static int DaysUntil(DateTime expiresAtUtc, DateTime now)
    {
        return (expiresAtUtc.Date - now.Date).Days;
    }

    /// <summary>
    /// The smallest threshold that <paramref name="daysUntil"/> has reached, or <c>null</c> when
    /// the token is still further out than every threshold.
    /// </summary>
    internal static int? GetDueThreshold(int daysUntil, IReadOnlyList<int> thresholds)
    {
        var reached = thresholds.Where(t => daysUntil <= t).ToList();
        return reached.Count == 0 ? null : reached.Min();
    }
}
