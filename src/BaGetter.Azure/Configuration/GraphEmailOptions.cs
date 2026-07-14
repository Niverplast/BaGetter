namespace BaGetter.Azure.Configuration;

/// <summary>
/// Microsoft Graph email settings, bound to the <c>Email</c> configuration section.
/// Used when <c>Email:Type</c> is <c>Graph</c>.
/// </summary>
public class GraphEmailOptions
{
    /// <summary>
    /// The mailbox to send as: a user's object id or user principal name.
    /// Graph sends via this mailbox's <c>sendMail</c> endpoint.
    /// </summary>
    public string SenderUserId { get; set; }

    /// <summary>
    /// Optional tenant id for client-secret authentication (local dev). When
    /// <see cref="TenantId"/>, <see cref="ClientId"/>, and <see cref="ClientSecret"/>
    /// are all set, a client-secret credential is used; otherwise the deployed
    /// managed identity (DefaultAzureCredential) is used.
    /// </summary>
    public string TenantId { get; set; }

    /// <summary>Optional client (application) id for client-secret authentication.</summary>
    public string ClientId { get; set; }

    /// <summary>Optional client secret for client-secret authentication.</summary>
    public string ClientSecret { get; set; }
}
