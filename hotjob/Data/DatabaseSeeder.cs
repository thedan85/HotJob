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
            // Insert by name rather than only-on-empty so existing dev databases pick up
            // catalog additions. Admin edits to an existing row are never overwritten.
            var categoryNames = await db.JobCategories
                .Select(category => category.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var missingCategories = JobCategoryCatalog
                .Where(name => !categoryNames.Contains(name))
                .Select(name => new Models.Job.JobCategory { Name = name })
                .ToList();
            if (missingCategories.Count > 0)
            {
                await db.JobCategories.AddRangeAsync(missingCategories, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Seeded {Count} job categories.", missingCategories.Count);
            }

            var skillNames = await db.Skills
                .Select(skill => skill.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var missingSkills = SkillCatalog
                .Where(skill => !skillNames.Contains(skill.Name))
                .Select(skill => new Models.Skill { Name = skill.Name, Category = skill.Category })
                .ToList();
            if (missingSkills.Count > 0)
            {
                await db.Skills.AddRangeAsync(missingSkills, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Seeded {Count} skills.", missingSkills.Count);
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

    /// <summary>Job category catalog shared by public browse filters and employer forms.</summary>
    private static readonly string[] JobCategoryCatalog =
    [
        "IT - Software",
        "Data & Analytics",
        "DevOps & Cloud",
        "Mobile",
        "QA & Testing",
        "Product",
        "Design",
        "Marketing",
        "Cybersecurity",
        "IT Support"
    ];

    /// <summary>Skill catalog with coarse categories, sized for realistic resume/job matching.</summary>
    private static readonly (string Name, string Category)[] SkillCatalog =
    [
        ("C#", "Backend"),
        ("ASP.NET Core", "Backend"),
        (".NET", "Backend"),
        ("SQL", "Database"),
        ("MySQL", "Database"),
        ("PostgreSQL", "Database"),
        ("MongoDB", "Database"),
        ("Redis", "Database"),
        ("Elasticsearch", "Database"),
        ("JavaScript", "Frontend"),
        ("TypeScript", "Frontend"),
        ("React", "Frontend"),
        ("Next.js", "Frontend"),
        ("Vue.js", "Frontend"),
        ("Angular", "Frontend"),
        ("HTML", "Frontend"),
        ("CSS", "Frontend"),
        ("Tailwind CSS", "Frontend"),
        ("Node.js", "Backend"),
        ("Java", "Backend"),
        ("Spring Boot", "Backend"),
        ("Python", "Backend"),
        ("Django", "Backend"),
        ("FastAPI", "Backend"),
        ("Go", "Backend"),
        ("PHP", "Backend"),
        ("Laravel", "Backend"),
        ("Kotlin", "Mobile"),
        ("Swift", "Mobile"),
        ("Flutter", "Mobile"),
        ("React Native", "Mobile"),
        ("Docker", "DevOps"),
        ("Kubernetes", "DevOps"),
        ("AWS", "Cloud"),
        ("Azure", "Cloud"),
        ("GCP", "Cloud"),
        ("Terraform", "DevOps"),
        ("CI/CD", "DevOps"),
        ("Linux", "DevOps"),
        ("RabbitMQ", "Messaging"),
        ("Kafka", "Messaging"),
        ("Git", "Tools"),
        ("Machine Learning", "Data"),
        ("TensorFlow", "Data"),
        ("PyTorch", "Data"),
        ("Pandas", "Data"),
        ("Power BI", "Data"),
        ("Manual Testing", "QA"),
        ("Selenium", "QA"),
        ("Playwright", "QA"),
        ("Cypress", "QA"),
        ("Figma", "Design"),
        ("UI/UX Design", "Design"),
        ("Product Management", "Product"),
        ("Agile/Scrum", "Product"),
        ("Business Analysis", "Product"),
        ("Digital Marketing", "Marketing"),
        ("SEO", "Marketing"),
        ("Content Marketing", "Marketing"),
        ("Cybersecurity", "Security"),
        ("Windows Server", "IT Support"),
        ("Communication", "Soft Skills"),
        ("English", "Soft Skills")
    ];

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
