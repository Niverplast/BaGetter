namespace BaGetter.Core.Configuration;

/// <summary>
/// SMTP settings, bound to the <c>Email</c> configuration section.
/// Used when <see cref="EmailOptions.Type"/> is <c>Smtp</c>.
/// </summary>
public class SmtpEmailOptions
{
    /// <summary>The SMTP server host name.</summary>
    public string Host { get; set; }

    /// <summary>The SMTP server port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// When <c>true</c>, upgrade the connection with STARTTLS; otherwise let
    /// MailKit auto-negotiate the most secure option the server supports.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>Optional SMTP username. When empty, no authentication is attempted.</summary>
    public string Username { get; set; }

    /// <summary>Optional SMTP password.</summary>
    public string Password { get; set; }
}
