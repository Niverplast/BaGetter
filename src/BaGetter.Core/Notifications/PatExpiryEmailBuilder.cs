using System;
using BaGetter.Core.Configuration;
using BaGetter.Core.Email;
using BaGetter.Core.Entities;
using Microsoft.Extensions.Options;

namespace BaGetter.Core.Notifications;

/// <inheritdoc />
public class PatExpiryEmailBuilder : IPatExpiryEmailBuilder
{
    private readonly PatExpiryNotificationOptions _options;

    public PatExpiryEmailBuilder(IOptions<PatExpiryNotificationOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public EmailMessage Build(PersonalAccessToken token, int daysUntilExpiry)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.User is null || string.IsNullOrEmpty(token.User.Email))
            throw new ArgumentException(
                "Token must have an associated user with an email address. Load the token with Include(t => t.User).",
                nameof(token));

        var whenText = daysUntilExpiry <= 0
            ? "today"
            : daysUntilExpiry == 1 ? "in 1 day" : $"in {daysUntilExpiry} days";

        var subject = $"Your BaGetter token '{token.Name}' expires {whenText}";

        var tokensLink = BuildTokensPageLink(_options.WebBaseUrl);
        var callToAction = tokensLink is null
            ? "Create a replacement token on your account's Tokens page before then."
            : $"""<a href=\"{System.Net.WebUtility.HtmlEncode(tokensLink)}\">Create a replacement token</a> before then.""";

        var tokenNameHtml = System.Net.WebUtility.HtmlEncode(token.Name);
        var tokenPrefixHtml = System.Net.WebUtility.HtmlEncode(token.TokenPrefix);

        var body =
            $"""
            <p>Your BaGetter personal access token <strong>{tokenNameHtml}</strong> (prefix <code>{tokenPrefixHtml}</code>) expires {whenText}, on <strong>{token.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC</strong>.</p>
            <p>Once it expires, any client using it will start failing authentication. {callToAction}</p>
            """;

        return new EmailMessage(token.User.Email, subject, body);
    }

    /// <summary>
    /// Builds an absolute URL to the token management page from the configured base URL,
    /// or <c>null</c> when no base URL is configured.
    /// </summary>
    private static string BuildTokensPageLink(string webBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(webBaseUrl))
            return null;

        return $"{webBaseUrl.TrimEnd('/')}/account/tokens";
    }
}
