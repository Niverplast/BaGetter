namespace BaGetter.Core.Configuration;

/// <summary>
/// Common email settings, bound to the <c>Email</c> configuration section.
/// Provider-specific settings bind to the same section (see
/// <see cref="SmtpEmailOptions"/>).
/// </summary>
public class EmailOptions
{
    /// <summary>
    /// The active email backend: <c>Smtp</c>, <c>Graph</c>, or <c>Null</c>.
    /// When unset, email sending is disabled.
    /// </summary>
    public string Type { get; set; }

    /// <summary>The address messages are sent from.</summary>
    public string FromAddress { get; set; }

    /// <summary>The display name messages are sent from.</summary>
    public string FromName { get; set; }
}
