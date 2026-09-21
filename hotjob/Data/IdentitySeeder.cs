using hotjob.Models.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace hotjob.Data;

/// <summary>Role names used across the application.</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Candidate = "Candidate";
    public const string Employer = "Employer";
}

/// <summary>Strongly-typed admin seed settings. Bind from configuration; never hardcode credentials.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Admin email used for the initial admin account.</summary>
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>Admin password (provide via user-secrets or env vars, never source control).</summary>
    public string AdminPassword { get; set; } = string.Empty;
}

/// <summary>Seeds Identity roles and the initial admin account.</summary>
public static class IdentitySeeder
{
    /// <summary>Creates roles and the admin user if missing. Idempotent.</summary>
    /// <param name="roleManager">Identity role manager.</param>
    /// <param name="userManager">Identity user manager.</param>
    /// <param name="seed">Admin seed settings from configuration.</param>
    /// <param name="logger">Logger to record what was seeded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SeedAsync(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        SeedOptions seed,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleManager);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            foreach (var role in new[] { AppRoles.Admin, AppRoles.Candidate, AppRoles.Employer })
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await roleManager.RoleExistsAsync(role).ConfigureAwait(false))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(role)).ConfigureAwait(false);
                    result.ThrowIfFailed();
                    logger.LogInformation("Created role {Role}.", role);
                }
            }

            if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
            {
                logger.LogWarning(
                    "Identity roles were seeded, but the admin account was skipped because {Section}:AdminEmail and {Section}:AdminPassword are not configured.",
                    SeedOptions.SectionName,
                    SeedOptions.SectionName);
                return;
            }

            if (await userManager.FindByEmailAsync(seed.AdminEmail).ConfigureAwait(false) is null)
            {
                var admin = new ApplicationUser { UserName = seed.AdminEmail, Email = seed.AdminEmail, EmailConfirmed = true };
                (await userManager.CreateAsync(admin, seed.AdminPassword).ConfigureAwait(false)).ThrowIfFailed();
                (await userManager.AddToRoleAsync(admin, AppRoles.Admin).ConfigureAwait(false)).ThrowIfFailed();
                logger.LogInformation("Seeded admin account {Email}.", seed.AdminEmail);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Identity seeding failed.");
            throw;
        }
    }

    private static void ThrowIfFailed(this IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
