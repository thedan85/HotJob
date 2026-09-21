using System.ComponentModel.DataAnnotations;

namespace hotjob.Services.Authentication;

/// <summary>Strongly-typed <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC signing key. Must be at least 32 characters (256-bit).</summary>
    [Required, MinLength(32)]
    public string SecretKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Access-token lifetime in minutes.</summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>Refresh-token lifetime in days. Preferred key.</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Legacy key (<c>Jwt:SessionTokenDays</c>). Honored when <see cref="RefreshTokenDays"/> is unset.</summary>
    public int SessionTokenDays { get; set; }

    public int EffectiveRefreshTokenDays =>
        RefreshTokenDays > 0 ? RefreshTokenDays
        : SessionTokenDays > 0 ? SessionTokenDays
        : 7;

    public int EffectiveAccessTokenMinutes =>
        AccessTokenMinutes > 0 ? AccessTokenMinutes : 60;
}
