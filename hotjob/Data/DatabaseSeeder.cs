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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reference data seeding failed.");
            throw;
        }
    }
}
