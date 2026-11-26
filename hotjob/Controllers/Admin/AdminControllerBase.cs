using System.Security.Claims;
using hotjob.Data;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

/// <summary>
/// Base for every <c>api/admin/*</c> controller: JWT-authenticated, Admin role only.
/// The acting admin is always taken from the validated token, never from the request body,
/// so a caller cannot attribute an action to another administrator.
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = AppRoles.Admin, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public abstract class AdminControllerBase : ControllerBase
{
    /// <summary>The authenticated admin's user id, or <c>null</c> when the token carries none.</summary>
    protected string? CurrentAdminUserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
    }

    /// <summary>Returned when a token passed role checks but has no usable subject claim.</summary>
    protected IActionResult MissingAdminIdentity() =>
        Unauthorized(new[] { "The access token does not identify an administrator." });

    /// <summary>200 with the payload, or the status code matching the failure.</summary>
    protected IActionResult OkOrFailure<T>(ServiceResult<T> result) =>
        result.Success ? Ok(result.Data) : FromFailure(result);

    /// <summary>204 on success, or the status code matching the failure.</summary>
    protected IActionResult NoContentOrFailure(ServiceResult result) =>
        result.Success ? NoContent() : FromFailure(result);

    /// <summary>NotFound 404, Conflict 409 (wrong current state), Unauthorized 401, otherwise 400.</summary>
    protected IActionResult FromFailure(ServiceResult result) =>
        result.ErrorType switch
        {
            ServiceErrorType.NotFound => NotFound(result.Errors),
            ServiceErrorType.Conflict => Conflict(result.Errors),
            ServiceErrorType.Unauthorized => Unauthorized(result.Errors),
            _ => BadRequest(result.Errors)
        };
}
