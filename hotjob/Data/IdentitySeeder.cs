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
public sealed class SeedAdminAccount
{
    /// <summary>Admin email used for the account.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Admin password (provide via user-secrets or env vars, never source control).</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Demo dataset for local Swagger testing (one company, one employer, one job, one candidate+resume+application).</summary>
public sealed class SeedDemoOptions
{
    public string CompanyName { get; set; } = string.Empty;
    public string EmployerEmail { get; set; } = string.Empty;
    public string EmployerPassword { get; set; } = string.Empty;
    public string EmployerName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string CandidatePassword { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
}

/// <summary>Strongly-typed admin seed settings. Bind from configuration; never hardcode credentials.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Legacy single-admin email. Prefer <see cref="Admins"/> for multiple accounts.</summary>
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>Legacy single-admin password (provide via user-secrets or env vars, never source control).</summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>Admin accounts to seed. Bound from Seed:Admins (appsettings) or Seed__Admins__N__Email/Password (env).</summary>
    public List<SeedAdminAccount> Admins { get; set; } = new();

    /// <summary>Demo dataset for local Swagger testing. Bound from Seed:Demo. Skipped when CompanyName is empty.</summary>
    public SeedDemoOptions Demo { get; set; } = new();
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

            var seeders = seed.Admins
                .Where(a => !string.IsNullOrWhiteSpace(a.Email) && !string.IsNullOrWhiteSpace(a.Password))
                .Select(a => new SeedAdminAccount { Email = a.Email.Trim(), Password = a.Password })
                .ToList();

            if (!string.IsNullOrWhiteSpace(seed.AdminEmail) && !string.IsNullOrWhiteSpace(seed.AdminPassword))
            {
                seeders.Add(new SeedAdminAccount { Email = seed.AdminEmail.Trim(), Password = seed.AdminPassword });
            }

            if (seeders.Count == 0)
            {
                logger.LogWarning(
                    "Identity roles were seeded, but no admin account was created because {Section}:Admins (or legacy {Section}:AdminEmail/AdminPassword) is not configured.",
                    SeedOptions.SectionName,
                    SeedOptions.SectionName);
                return;
            }

            foreach (var admin in seeders.DistinctBy(a => a.Email.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SeedOneAsync(userManager, admin, logger, cancellationToken).ConfigureAwait(false);
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

    private static async Task SeedOneAsync(
        UserManager<ApplicationUser> userManager,
        SeedAdminAccount admin,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(admin.Email).ConfigureAwait(false) is not null)
        {
            return;
        }

        var user = new ApplicationUser { UserName = admin.Email, Email = admin.Email, EmailConfirmed = true };
        (await userManager.CreateAsync(user, admin.Password).ConfigureAwait(false)).ThrowIfFailed();
        (await userManager.AddToRoleAsync(user, AppRoles.Admin).ConfigureAwait(false)).ThrowIfFailed();
        logger.LogInformation("Seeded admin account {Email}.", admin.Email);
    }

    private static void ThrowIfFailed(this IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
