using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Authentication;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshRequest
{
    /// <summary>Fallback when the refresh cookie is absent (non-browser clients).</summary>
    [MaxLength(256)]
    public string? RefreshToken { get; set; }
}

/// <summary>Login/refresh response body. The refresh token itself travels only in the HttpOnly cookie.</summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string UserId,
    string Email,
    IReadOnlyList<string> Roles,
    string? DisplayName = null);
