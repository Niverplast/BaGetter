using System;

namespace BaGetter.Core.Entities;

public class PersonalAccessToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; }
    public string TokenHash { get; set; }
    public string TokenPrefix { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public bool IsRevoked { get; set; }

    /// <summary>
    /// The smallest expiry-notification threshold (in days before expiry) that has
    /// already been emailed for this token, or <c>null</c> if none has been sent yet.
    /// Used by the expiry-notification scanner to avoid re-sending the same warning.
    /// </summary>
    public int? ExpiryNotificationThresholdDays { get; set; }

    public User User { get; set; }
}
