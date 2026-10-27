using hotjob.Models.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Data;

/// <summary>Seeds reference data (job categories, skills) on first run.</summary>
public static class DatabaseSeeder
{
    /// <summary>Seeds lookup tables if empty. Idempotent and safe to call on every startup.</summary>
    /// <param name="db">Application database context.</param>
    /// <param name="logger">Logger to record what was seeded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SeedAsync(
        ApplicationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            if (!await db.JobCategories.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                await db.JobCategories.AddRangeAsync(
                [
                    new Models.Job.JobCategory { Name = "IT - Software" },
                    new Models.Job.JobCategory { Name = "Design" },
                    new Models.Job.JobCategory { Name = "Marketing" },
                ], cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Seeded job categories.");
            }

            if (!await db.Skills.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                await db.Skills.AddRangeAsync(
                [
                    new Models.Skill { Name = "C#", Category = "Backend" },
                    new Models.Skill { Name = "SQL", Category = "Database" },
                    new Models.Skill { Name = "React", Category = "Frontend" },
                ], cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Seeded skills.");
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await SeedPlansAsync(db, logger, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reference data seeding failed.");
            throw;
        }
    }

    /// <summary>The plan catalogue (contract D3). Prices here are only the initial values; admins may edit them later.</summary>
    public static IReadOnlyList<Plan> DefaultPlans() =>
    [
        new() { Code = Plan.FreeCode, Audience = PlanAudience.Employer, Name = "Free", Price = 0, JobQuota = 1, DurationDays = 0, SortOrder = 0 },
        new() { Code = Plan.StarterCode, Audience = PlanAudience.Employer, Name = "Starter", Price = 500_000, JobQuota = 5, DurationDays = 30, SortOrder = 1 },
        new() { Code = Plan.GrowthCode, Audience = PlanAudience.Employer, Name = "Growth", Price = 1_500_000, JobQuota = 20, DurationDays = 30, SortOrder = 2 },
        new() { Code = Plan.PremiumMonthlyCode, Audience = PlanAudience.Candidate, Name = "Premium Monthly", Price = 99_000, JobQuota = 0, DurationDays = 30, SortOrder = 10 },
    ];

    // ponytail: insert-by-Code only. An existing row (possibly edited by an admin) is never touched.
    private static async Task SeedPlansAsync(ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var existing = await db.Plans.Select(x => x.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        var missing = DefaultPlans().Where(x => !existing.Contains(x.Code)).ToList();
        if (missing.Count > 0)
        {
            await db.Plans.AddRangeAsync(missing, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded plans: {Codes}.", string.Join(", ", missing.Select(x => x.Code)));
        }

        // Back-fill: companies that predate billing have no plan; make FREE explicit. Null still means FREE at runtime.
        var freeId = await db.Plans.Where(x => x.Code == Plan.FreeCode).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (freeId is not null)
        {
            var updated = await db.CompanyProfiles
                .Where(x => x.PlanId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlanId, freeId), cancellationToken)
                .ConfigureAwait(false);
            if (updated > 0)
            {
                logger.LogInformation("Assigned the FREE plan to {Count} companies.", updated);
            }
        }
    }
}
