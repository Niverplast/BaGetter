using System;
using System.Collections.Generic;

namespace BaGetter.Core.Email;

/// <summary>
/// An email to send. The sender ("from") is taken from configuration
/// (see <see cref="Configuration.EmailOptions"/>), not from the message.
/// </summary>
public class EmailMessage
{
    public EmailMessage(IReadOnlyList<string> to, string subject, string body, bool isBodyHtml = true)
    {
        To = to ?? throw new ArgumentNullException(nameof(to));
        Subject = subject;
        Body = body;
        IsBodyHtml = isBodyHtml;
    }

    public EmailMessage(string to, string subject, string body, bool isBodyHtml = true)
        : this(new[] { to }, subject, body, isBodyHtml)
    {
    }

    /// <summary>The recipient address(es).</summary>
    public IReadOnlyList<string> To { get; }

    /// <summary>The subject line.</summary>
    public string Subject { get; }

    /// <summary>The message body, interpreted as HTML or plain text per <see cref="IsBodyHtml"/>.</summary>
    public string Body { get; }

    /// <summary>Whether <see cref="Body"/> is HTML (otherwise plain text).</summary>
    public bool IsBodyHtml { get; }
}
