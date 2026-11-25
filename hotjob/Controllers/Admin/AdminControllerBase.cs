using System.Security.Claims;
using hotjob.Data;
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
}
