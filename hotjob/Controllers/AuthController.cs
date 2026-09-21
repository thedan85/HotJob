using System.Security.Claims;
using hotjob.DTOs.Authentication;
using hotjob.Services.Authentication;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    public const string RefreshCookieName = "hotjob_refresh";
    private const string RefreshCookiePath = "/api/auth";

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

    /// <summary>Revokes ALL refresh sessions for the user (logout everywhere) and clears the cookie.</summary>
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
        new(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.UserId, tokens.Email, tokens.Roles);

    private static IActionResult MapError(ServiceResult result) =>
        result.ErrorType switch
        {
            ServiceErrorType.Conflict => new ConflictObjectResult(result.Errors),
            ServiceErrorType.NotFound => new NotFoundObjectResult(result.Errors),
            ServiceErrorType.Unauthorized => new UnauthorizedObjectResult(result.Errors),
            _ => new BadRequestObjectResult(result.Errors)
        };
}
