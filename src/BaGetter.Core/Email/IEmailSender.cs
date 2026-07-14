using System.Threading;
using System.Threading.Tasks;

namespace BaGetter.Core.Email;

/// <summary>
/// Sends emails. Implementations are selected at runtime through the provider
/// pattern using the <c>Email:Type</c> configuration key (see <see cref="Configuration.EmailOptions"/>).
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Send an email message.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
