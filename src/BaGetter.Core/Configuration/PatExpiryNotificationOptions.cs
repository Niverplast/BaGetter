using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BaGetter.Core.Configuration;

/// <summary>
/// Configuration for the background service that emails personal access token owners
/// before their tokens expire. Bound to the <c>PatExpiryNotification</c> section.
/// </summary>
public class PatExpiryNotificationOptions : IValidatableObject
{
    /// <summary>
    /// Whether the expiry-notification scanner runs. When <c>false</c>, no scanning or
    /// emailing happens (regardless of email configuration).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the scanner wakes up to look for tokens nearing expiry.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ScanIntervalHours { get; set; } = 1;

    /// <summary>The thresholds applied when <see cref="NotificationDaysBeforeExpiry"/> is not configured.</summary>
    public static readonly int[] DefaultNotificationDaysBeforeExpiry = [14, 7, 2, 0];

    /// <summary>
    /// The thresholds, in whole days before expiry, at which an owner is emailed.
    /// A separate warning is sent as the token crosses into each threshold. <c>0</c>
    /// means "on the expiry day itself". When unset, <see cref="DefaultNotificationDaysBeforeExpiry"/> is used.
    /// </summary>
    /// <remarks>
    /// No default initializer here on purpose: the configuration binder <em>appends</em> to a
    /// pre-populated array rather than replacing it, so a default of <c>[14,7,2,0]</c> plus the same
    /// values in config would yield duplicates. Resolve the effective list via <see cref="EffectiveNotificationDays"/>.
    /// </remarks>
    public int[] NotificationDaysBeforeExpiry { get; set; }

    /// <summary>The thresholds to use, falling back to <see cref="DefaultNotificationDaysBeforeExpiry"/> when none are configured.</summary>
    public IReadOnlyList<int> EffectiveNotificationDays =>
        NotificationDaysBeforeExpiry is { Length: > 0 } days ? days : DefaultNotificationDaysBeforeExpiry;

    /// <summary>
    /// The public base URL of the BaGetter site (e.g. <c>https://packages.example.com</c>).
    /// When set, notification emails include a link to the token management page so the owner
    /// can create a replacement token. When empty, the email refers to the page by name only.
    /// The scanner runs outside an HTTP request and cannot infer this, so it must be configured.
    /// </summary>
    public string WebBaseUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A null/empty array is allowed: it means "use the defaults". Only validate explicit values.
        if (NotificationDaysBeforeExpiry is { Length: > 0 } days)
        {
            if (days.Any(d => d < 0))
            {
                yield return new ValidationResult(
                    $"{nameof(NotificationDaysBeforeExpiry)} values must be zero or greater.",
                    [nameof(NotificationDaysBeforeExpiry)]);
            }

            if (days.Distinct().Count() != days.Length)
            {
                yield return new ValidationResult(
                    $"{nameof(NotificationDaysBeforeExpiry)} values must be distinct.",
                    [nameof(NotificationDaysBeforeExpiry)]);
            }
        }

        if (!string.IsNullOrWhiteSpace(WebBaseUrl))
        {
            var isHttpUrl = Uri.TryCreate(WebBaseUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            if (!isHttpUrl)
            {
                yield return new ValidationResult(
                    $"{nameof(WebBaseUrl)} must be an absolute http(s) URL when set.",
                    [nameof(WebBaseUrl)]);
            }
        }
    }
}
