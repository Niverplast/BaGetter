using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BaGetter.Core.Email;

/// <summary>
/// An <see cref="IEmailSender"/> that delivers messages over SMTP using MailKit.
/// Active when <c>Email:Type</c> is <c>Smtp</c>.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _emailOptions;
    private readonly SmtpEmailOptions _smtpOptions;

    public SmtpEmailSender(
        IOptions<EmailOptions> emailOptions,
        IOptions<SmtpEmailOptions> smtpOptions)
    {
        _emailOptions = emailOptions?.Value ?? throw new ArgumentNullException(nameof(emailOptions));
        _smtpOptions = smtpOptions?.Value ?? throw new ArgumentNullException(nameof(smtpOptions));
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mimeMessage = BuildMimeMessage(message);

        var secureSocketOptions = _smtpOptions.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.Auto;

        using var client = new SmtpClient();
        await client.ConnectAsync(_smtpOptions.Host, _smtpOptions.Port, secureSocketOptions, cancellationToken);

        if (!string.IsNullOrEmpty(_smtpOptions.Username))
        {
            // Never transmit credentials over an unencrypted link. SecureSocketOptions.Auto
            // (used when UseStartTls is false) can silently fall back to a plaintext connection
            // when the server does not advertise STARTTLS, which would leak the credentials.
            if (!client.IsSecure)
            {
                throw new InvalidOperationException(
                    "Refusing to send SMTP credentials over an unencrypted connection. " +
                    "Enable UseStartTls or use a server that supports TLS.");
            }

            await client.AuthenticateAsync(_smtpOptions.Username, _smtpOptions.Password, cancellationToken);
        }

        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>
    /// Translates an <see cref="EmailMessage"/> into a MailKit <see cref="MimeMessage"/>,
    /// taking the sender from configuration and the body kind from <see cref="EmailMessage.IsBodyHtml"/>.
    /// </summary>
    internal MimeMessage BuildMimeMessage(EmailMessage message)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(_emailOptions.FromName, _emailOptions.FromAddress));
        foreach (var recipient in message.To)
        {
            mimeMessage.To.Add(MailboxAddress.Parse(recipient));
        }

        mimeMessage.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder();
        if (message.IsBodyHtml)
        {
            bodyBuilder.HtmlBody = message.Body;
        }
        else
        {
            bodyBuilder.TextBody = message.Body;
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();
        return mimeMessage;
    }
}
