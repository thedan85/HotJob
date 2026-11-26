using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using hotjob.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Authentication;

/// <summary>
/// JWT bearer hook: a signature-valid access token is still rejected when its user no longer
/// exists or has been disabled, so locking an account takes effect on the next API call
/// instead of when the token expires.
/// </summary>
public static class ActiveAccountTokenValidation
{
    public static async Task RejectInactiveAccountAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Fail("The access token does not identify a user.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var isActive = await db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!isActive)
        {
            context.Fail("Account is disabled.");
        }
    }
}
