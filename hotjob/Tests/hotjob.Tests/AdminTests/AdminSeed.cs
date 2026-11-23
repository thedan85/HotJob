using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Tests.Support;

namespace hotjob.Tests.AdminTests;

/// <summary>Seeding helpers for admin moderation tests (real FK targets, SQLite in-memory).</summary>
internal static class AdminSeed
{
    public const string Password = "Str0ng!Pass";

    public static async Task<string> AdminAsync(TestFixture fx, string email = "admin@example.com") =>
        (await fx.SeedUserAsync(email, Password, AppRoles.Admin)).Id;

    public static async Task<CompanyProfile> CompanyAsync(
        TestFixture fx,
        string name,
        CompanyStatus status = CompanyStatus.Pending,
        bool isActive = true,
        string? industry = null)
    {
        var company = new CompanyProfile
        {
            CompanyName = name,
            Status = status,
            IsActive = isActive,
            Industry = industry
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        return company;
    }

    public static async Task<Employer> EmployerAsync(
        TestFixture fx,
        int companyId,
        string email,
        string fullName,
        bool isOwner = false,
        MembershipStatus membership = MembershipStatus.Active)
    {
        var user = await fx.SeedUserAsync(email, Password, AppRoles.Employer);
        var employer = new Employer
        {
            UserId = user.Id,
            CompanyProfileId = companyId,
            FullName = fullName,
            IsOwner = isOwner,
            MembershipStatus = membership
        };
        fx.Db.Employers.Add(employer);
        await fx.Db.SaveChangesAsync();
        return employer;
    }

    public static async Task<Candidate> CandidateAsync(TestFixture fx, string email, string fullName)
    {
        var user = await fx.SeedUserAsync(email, Password, AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = fullName };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();
        return candidate;
    }

    public static async Task<Review> ReviewAsync(
        TestFixture fx,
        int companyId,
        int candidateId,
        int rating,
        string? text = null,
        bool isDeleted = false,
        DateTime? createdAt = null)
    {
        var review = new Review
        {
            CompanyProfileId = companyId,
            CandidateId = candidateId,
            RatingScore = rating,
            ReviewText = text,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null,
            DeletionReason = isDeleted ? "seeded" : null,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        fx.Db.Reviews.Add(review);
        await fx.Db.SaveChangesAsync();
        return review;
    }

    public static async Task<JobPosting> JobAsync(
        TestFixture fx,
        int companyId,
        JobStatus status,
        string title = "Backend Developer")
    {
        var job = new JobPosting
        {
            CompanyProfileId = companyId,
            Title = title,
            Description = "Job description",
            Status = status
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        return job;
    }
}
