namespace BaGetter.Core.Configuration;

public sealed class NugetAuthenticationOptions
{
    /// <summary>
    /// Controls which authentication mechanisms are active.
    /// Defaults to <see cref="AuthenticationMode.None"/> for backward compatibility.
    /// </summary>
    public AuthenticationMode Mode { get; set; } = AuthenticationMode.None;

    /// <summary>
    /// Azure Entra ID (OIDC) configuration. Required when Mode is Entra or Hybrid.
    /// </summary>
    public EntraOptions Entra { get; set; }

    /// <summary>
    /// Maximum number of days a personal access token can be valid.
    /// </summary>
    public int MaxTokenExpiryDays { get; set; } = 365;

    /// <summary>
    /// Number of consecutive failed login attempts before a local account is locked out.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Duration in minutes that a local account remains locked after exceeding the failed login threshold.
    /// </summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Username and password credentials for downloading packages (legacy, used when Mode is None).
    /// </summary>
    public NugetCredentials[] Credentials { get; set; }

    /// <summary>
    /// Api keys for pushing packages into the feed (legacy, used when Mode is None).
    /// </summary>
    public ApiKey[] ApiKeys { get; set; }
}
