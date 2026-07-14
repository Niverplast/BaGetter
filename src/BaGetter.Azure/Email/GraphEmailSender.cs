using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Azure.Configuration;
using BaGetter.Core.Email;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace BaGetter.Azure.Email;

/// <summary>
/// An <see cref="IEmailSender"/> that delivers messages through Microsoft Graph
/// (<c>users/{id}/sendMail</c>). Active when <c>Email:Type</c> is <c>Graph</c>.
/// Requires the <c>Mail.Send</c> application permission on the identity used by
/// the configured <see cref="GraphServiceClient"/>.
/// </summary>
public class GraphEmailSender : IEmailSender
{
    private readonly GraphServiceClient _graphClient;
    private readonly GraphEmailOptions _options;

    public GraphEmailSender(GraphServiceClient graphClient, IOptions<GraphEmailOptions> options)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mail = new Message
        {
            Subject = message.Subject,
            Body = new ItemBody
            {
                ContentType = message.IsBodyHtml ? BodyType.Html : BodyType.Text,
                Content = message.Body,
            },
            ToRecipients = message.To
                .Select(address => new Recipient
                {
                    EmailAddress = new EmailAddress { Address = address },
                })
                .ToList(),
        };

        var requestBody = new SendMailPostRequestBody
        {
            Message = mail,
            SaveToSentItems = false,
        };

        await _graphClient
            .Users[_options.SenderUserId]
            .SendMail
            .PostAsync(requestBody, cancellationToken: cancellationToken);
    }
}
