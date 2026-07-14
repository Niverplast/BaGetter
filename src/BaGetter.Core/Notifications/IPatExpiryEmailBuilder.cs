using BaGetter.Core.Email;
using BaGetter.Core.Entities;

namespace BaGetter.Core.Notifications;

/// <summary>
/// Builds the notification email sent to a token owner as expiry approaches.
/// </summary>
public interface IPatExpiryEmailBuilder
{
    /// <summary>
    /// Build the expiry-notification message for a token.
    /// </summary>
    /// <param name="token">The token nearing expiry; its <see cref="PersonalAccessToken.User"/> supplies the recipient.</param>
    /// <param name="daysUntilExpiry">Whole days until the token expires (<c>0</c> on the expiry day, negative once past).</param>
    EmailMessage Build(PersonalAccessToken token, int daysUntilExpiry);
}
