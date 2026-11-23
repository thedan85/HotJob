using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using hotjob.Data;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Authentication;

/// <summary>
/// Per-request check that the account behind a still-valid access token exists and is not disabled,
/// so disabling an account takes effect immediately instead of when the access token expires.
/// </summary>
public static class ActiveUserValidator
{
    public static async Task<bool> IsActiveAsync(
        ApplicationDbContext db,
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken = default)
    {
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return await db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken);
    }
}
