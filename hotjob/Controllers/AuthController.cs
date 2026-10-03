using System.Security.Claims;
using hotjob.DTOs.Authentication;
using hotjob.Services.Authentication;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

[ApiController]
[Route("api/auth")]
[IgnoreAntiforgeryToken]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    public const string RefreshCookieName = "hotjob_refresh";
    private const string RefreshCookiePath = "/api/auth";

    /// <summary>Login with email + password. Sets the HttpOnly refresh cookie, returns access token + user + roles.</summary>
    /// <response code="200">Authenticated. Access token in body, refresh token in cookie.</response>
    /// <response code="400">Email/password missing.</response>
    /// <response code="401">Bad credentials, locked out, or disabled account.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        SetRefreshCookie(result.Data!.RefreshToken, result.Data.RefreshTokenExpiresAtUtc);
        return Ok(ToResponse(result.Data));
    }

    /// <summary>Rotate the refresh token (cookie or body fallback). Revokes the presented token, issues a new pair.</summary>
    /// <response code="200">New access token + rotated refresh cookie.</response>
    /// <response code="401">Missing, invalid, reused, or expired refresh token (reuse revokes all sessions).</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest? request,
        CancellationToken cancellationToken)
    {
        var rawToken = Request.Cookies[RefreshCookieName] ?? request?.RefreshToken;
        var result = await authService.RefreshAsync(rawToken, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        SetRefreshCookie(result.Data!.RefreshToken, result.Data.RefreshTokenExpiresAtUtc);
        return Ok(ToResponse(result.Data));
    }

    /// <summary>Restores the UI session from the HttpOnly refresh cookie: validates WITHOUT rotating, returns a fresh access token.</summary>
    /// <response code="200">Valid cookie. Access token + user + roles (refresh token never in body).</response>
    /// <response code="401">Missing, invalid, revoked, or expired refresh token; disabled account.</response>
    [HttpGet("me")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await authService.GetSessionAsync(Request.Cookies[RefreshCookieName], cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        return Ok(ToResponse(result.Data!));
    }

    /// <summary>Logout everywhere: revokes ALL refresh sessions for the user and clears the cookie.</summary>
    /// <response code="204">Logged out.</response>
    /// <response code="401">Not authenticated (no user id or refresh token).</response>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        var result = await authService.LogoutAsync(Request.Cookies[RefreshCookieName], userId, cancellationToken);

        Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
        if (!result.Success)
        {
            return MapError(result);
        }

        return NoContent();
    }

    private void SetRefreshCookie(string rawToken, DateTime expiresAtUtc)
    {
        Response.Cookies.Append(RefreshCookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            Expires = new DateTimeOffset(expiresAtUtc)
        });
    }

    private static AuthResponse ToResponse(AuthTokens tokens) =>
        new(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.UserId, tokens.Email, tokens.Roles,
            tokens.DisplayName);

    private static IActionResult MapError(ServiceResult result) =>
        result.ErrorType switch
        {
            ServiceErrorType.Conflict => new ConflictObjectResult(result.Errors),
            ServiceErrorType.NotFound => new NotFoundObjectResult(result.Errors),
            ServiceErrorType.Unauthorized => new UnauthorizedObjectResult(result.Errors),
            _ => new BadRequestObjectResult(result.Errors)
        };
}
