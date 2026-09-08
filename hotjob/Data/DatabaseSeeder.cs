namespace hotjob.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        if (db.JobCategories.Any()) return;
        db.JobCategories.AddRange(
            new Models.Job.JobCategory { Name = "IT - Software" },
            new Models.Job.JobCategory { Name = "Design" },
            new Models.Job.JobCategory { Name = "Marketing" }
        );
        if (!db.Skills.Any())
            db.Skills.AddRange(
                new Models.Skill { Name = "C#", Category = "Backend" },
                new Models.Skill { Name = "SQL", Category = "Database" },
                new Models.Skill { Name = "React", Category = "Frontend" }
            );
        await db.SaveChangesAsync();
    }
}
