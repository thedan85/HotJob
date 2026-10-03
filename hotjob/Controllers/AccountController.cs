using System.Security.Claims;
using hotjob.DTOs.Authentication;
using hotjob.Services.Account;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

[ApiController]
[Route("api/account")]
public sealed class AccountController(IAccountService accountService) : ControllerBase
{
    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
            return Unauthorized("Not authenticated.");

        return MapProfileResult(
            await accountService.GetProfileAsync(userId, cancellationToken));
    }

    [HttpPut("profile")]
    [Authorize]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
            return Unauthorized("Not authenticated.");

        return MapProfileResult(
            await accountService.UpdateProfileAsync(userId, request, cancellationToken));
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
            return Unauthorized("Not authenticated.");

        var result = await accountService.ChangePasswordAsync(userId, request, cancellationToken);
        return result.Success ? NoContent() : MapError(result);
    }

    /// <summary>Register a Candidate or Employer account. Employer joins an existing company (CompanyProfileId) or creates one (CompanyName).</summary>
    /// <response code="201">Created. Returns user id, profile flags, company id.</response>
    /// <response code="400">Validation failed (email, password rules, company selection).</response>
    /// <response code="409">Email or company name already exists.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisteredAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.RegisterAsync(request, cancellationToken);

        if (result.Success)
        {
            return StatusCode(StatusCodes.Status201Created, result.Data);
        }

        return MapError(result);
    }

    private string? GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private static IActionResult MapProfileResult(ServiceResult<ProfileResponse> result) =>
        result.Success ? new OkObjectResult(result.Data) : MapError(result);

    private static IActionResult MapError(ServiceResult result) =>
        result.ErrorType switch
        {
            ServiceErrorType.Conflict => new ConflictObjectResult(result.Errors),
            ServiceErrorType.NotFound => new NotFoundObjectResult(result.Errors),
            ServiceErrorType.Unauthorized => new UnauthorizedObjectResult(result.Errors),
            _ => new BadRequestObjectResult(result.Errors)
        };
}