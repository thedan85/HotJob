using System.Security.Claims;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

/// <summary>
/// Base for admin API controllers: JWT-only, Admin role, shared <see cref="ApiControllerBase"/> envelopes.
/// The acting admin is always read from the access token, never from the request body.
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public abstract class AdminControllerBase : ApiControllerBase
{
    /// <summary>Identity user id of the calling admin, from the <c>NameIdentifier</c> (or raw <c>sub</c>) claim.</summary>
    protected string? CurrentAdminId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    /// <summary>401 envelope for a token that carries no usable user id.</summary>
    protected IActionResult MissingAdminIdentity() =>
        FromError(ServiceResult.Fail(ServiceErrorType.Unauthorized, "Admin identity is missing from the access token."));

    /// <summary>Maps a unit <see cref="ServiceResult"/> to <c>204</c> on success or a status-coded envelope on failure.</summary>
    protected IActionResult FromUnitResult(ServiceResult result) =>
        result.Success ? NoContent() : FromError(result);

    /// <summary>
    /// Runs a mutating admin action with the caller's id, returning 401 when the token has no id,
    /// otherwise <c>204</c> / status-coded envelope.
    /// </summary>
    protected async Task<IActionResult> AsAdminAsync(Func<string, Task<ServiceResult>> action)
    {
        var adminId = CurrentAdminId();
        if (string.IsNullOrEmpty(adminId))
            return MissingAdminIdentity();

        return FromUnitResult(await action(adminId));
    }
}
