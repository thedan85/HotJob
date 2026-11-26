using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Tests.Support;

namespace hotjob.Tests.JobTests;

/// <summary>
/// The employer and public job lists are projected straight to DTOs in SQL; these pin every
/// derived field (counts, names, rejection reason) so the projection cannot silently drift.
/// </summary>
public sealed class JobListProjectionTests
{
    private sealed record Seed(int CompanyId, int PublishedJobId, int RejectedJobId);

    private static async Task<Seed> SeedAsync(TestFixture fx)
    {
        var company = new CompanyProfile { CompanyName = "Acme", Location = "Hanoi", Status = CompanyStatus.Approved };
        var backend = new JobCategory { Name = "Backend" };
        var cloud = new JobCategory { Name = "Cloud" };
        var csharp = new Skill { Name = "C#" };
        var sql = new Skill { Name = "SQL" };
        fx.Db.AddRange(company, backend, cloud, csharp, sql);
        await fx.Db.SaveChangesAsync();

        var published = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Backend engineer",
            Description = "Build APIs",
            SalaryMin = 1000,
            SalaryMax = 2000,
            ExperienceRequired = 2,
            JobType = JobType.FullTime,
            Status = JobStatus.Published,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            CategoryMappings = [new() { JobCategoryId = backend.Id }, new() { JobCategoryId = cloud.Id }],
            JobSkills = [new() { SkillId = csharp.Id }, new() { SkillId = sql.Id }]
        };
        var rejected = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Rejected role",
            Description = "Nope",
            JobType = JobType.PartTime,
            Status = JobStatus.Rejected,
            RejectionReason = "Missing salary",
            CreatedAt = DateTime.UtcNow
        };
        fx.Db.JobPostings.AddRange(published, rejected);
        await fx.Db.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
        {
            var user = await fx.SeedUserAsync($"cand{i}@example.com", role: AppRoles.Candidate);
            var candidate = new Candidate { UserId = user.Id, FullName = $"Cand {i}" };
            fx.Db.Candidates.Add(candidate);
            await fx.Db.SaveChangesAsync();
            var resume = new Resume { CandidateId = candidate.Id, Title = "CV" };
            fx.Db.Resumes.Add(resume);
            await fx.Db.SaveChangesAsync();
            fx.Db.Applications.Add(new Application
            {
                JobPostingId = published.Id,
                ResumeId = resume.Id,
                CandidateId = candidate.Id
            });
        }

        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();
        return new Seed(company.Id, published.Id, rejected.Id);
    }

    [Fact]
    public async Task ListOwned_ProjectsCountsNamesAndRejectionReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = await SeedAsync(fx);

        var (items, total) = await new JobPostingRepository(fx.Db)
            .ListOwnedAsync(seed.CompanyId, null, null, 1, 20);

        Assert.Equal(2, total);
        // Newest first.
        Assert.Equal([seed.RejectedJobId, seed.PublishedJobId], items.Select(item => item.Id));

        var published = items.Single(item => item.Id == seed.PublishedJobId);
        Assert.Equal("Acme", published.CompanyName);
        Assert.Equal(2, published.SkillsCount);
        Assert.Equal(["Backend", "Cloud"], published.CategoryNames.Order());
        Assert.Equal(3, published.ApplicationsCount);
        Assert.Equal(2, published.ExperienceRequired);
        Assert.Null(published.RejectionReason);

        var rejected = items.Single(item => item.Id == seed.RejectedJobId);
        Assert.Equal(0, rejected.ApplicationsCount);
        Assert.Empty(rejected.CategoryNames);
        Assert.Equal("Missing salary", rejected.RejectionReason);
    }

    [Fact]
    public async Task ListOwned_SearchesDescriptionWithoutLoadingIt()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = await SeedAsync(fx);

        var (items, total) = await new JobPostingRepository(fx.Db)
            .ListOwnedAsync(seed.CompanyId, null, "APIs", 1, 20);

        Assert.Equal(1, total);
        Assert.Equal(seed.PublishedJobId, Assert.Single(items).Id);
    }

    [Fact]
    public async Task BrowsePublished_ProjectsCardFields()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = await SeedAsync(fx);

        var (items, total) = await new JobPostingRepository(fx.Db)
            .BrowsePublishedAsync(null, null, null, null, null, null, null, null, 1, 20);

        Assert.Equal(1, total);
        var card = Assert.Single(items);
        Assert.Equal(seed.PublishedJobId, card.Id);
        Assert.Equal("Backend engineer", card.Title);
        Assert.Equal("Acme", card.CompanyName);
        Assert.Equal("Hanoi", card.CompanyLocation);
        Assert.Equal(1000, card.SalaryMin);
        Assert.Equal(2000, card.SalaryMax);
        Assert.Equal(JobType.FullTime, card.JobType);
        Assert.Equal(["C#", "SQL"], card.Skills.Order());
    }

    [Fact]
    public async Task BrowsePublished_PagesInTheDatabase()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = new CompanyProfile { CompanyName = "Acme", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(-10);
        for (var i = 0; i < 5; i++)
        {
            fx.Db.JobPostings.Add(new JobPosting
            {
                CompanyProfileId = company.Id,
                Title = $"Job {i}",
                Description = "d",
                Status = JobStatus.Published,
                CreatedAt = start.AddDays(i)
            });
        }
        await fx.Db.SaveChangesAsync();

        var (page2, total) = await new JobPostingRepository(fx.Db)
            .BrowsePublishedAsync(null, null, null, null, null, null, null, null, 2, 2);

        Assert.Equal(5, total);
        Assert.Equal(["Job 2", "Job 1"], page2.Select(item => item.Title));
    }
}
