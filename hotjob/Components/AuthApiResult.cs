using hotjob.DTOs.Authentication;
using hotjob.Services.Authentication;

namespace hotjob.Components;

/// <summary>
/// Envelope returned by <c>wwwroot/js/auth.js</c> after a browser-originated fetch.
/// Property names are camelCase in JSON; JS interop deserializes case-insensitively.
/// </summary>
public sealed class AuthApiResult
{
    public bool Ok { get; set; }

    public int Status { get; set; }

    public AuthResponse? Data { get; set; }

    public string[] Errors { get; set; } = [];

    public string ErrorMessage =>
        Errors.Length > 0
            ? string.Join(" ", Errors)
            : "We could not sign you in right now. Please try again.";

    /// <summary>
    /// Maps an API response to in-memory circuit tokens. The raw refresh token is never
    /// present in the body (HttpOnly cookie only), so it stays empty here — the cookie
    /// drives session restore (<c>GET /api/auth/me</c>) and logout.
    /// </summary>
    /// <exception cref="InvalidOperationException">Response body missing (<c>Data</c> is null).</exception>
    public AuthTokens ToTokens()
    {
        if (Data is null)
        {
            throw new InvalidOperationException("Auth response body is missing.");
        }

        return new AuthTokens(
            Data.AccessToken,
            Data.AccessTokenExpiresAtUtc,
            string.Empty,
            DateTime.UtcNow,
            Data.UserId,
            Data.Email,
            Data.Roles)
        {
            DisplayName = Data.DisplayName
        };
    }
}
