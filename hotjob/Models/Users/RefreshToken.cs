using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Users;

/// <summary>
/// Persisted refresh-token record. Only the SHA-256 hash is stored; the raw
/// token is handed to the client once (HttpOnly cookie) and never persisted.
/// Rotation: on each refresh the old row is revoked and linked to its replacement.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>SHA-256 hex of the raw refresh token. Unique.</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = default!;

    /// <summary>Identity user owning this token.</summary>
    [MaxLength(255)]
    public string UserId { get; set; } = default!;

    public ApplicationUser User { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Null while active. Set on rotation, logout, or theft response.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Hash of the replacement token when revoked via rotation. Null otherwise.</summary>
    [MaxLength(64)]
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Why the token was revoked (Rotated, Logout, ReuseDetected, ...).</summary>
    [MaxLength(200)]
    public string? RevokedReason { get; set; }

    public bool IsActive(DateTime now) => RevokedAtUtc is null && ExpiresAtUtc > now;
}
