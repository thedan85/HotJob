using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Common;
using hotjob.Services.Billing;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.JobTests;

public sealed class PublicJobDetailTests
{
    private static async Task<int> SeedJobAsync(
        TestFixture fx, JobStatus status, DateTime? expiryDate, string title = "Senior .NET Developer")
    {
        var company = new CompanyProfile
        {
            CompanyName = $"Co {Guid.NewGuid():N}",
            Location = "Ho Chi Minh",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(company);
        var skill = new Skill { Name = $"C# {Guid.NewGuid():N}" };
        fx.Db.Skills.Add(skill);
        var category = new JobCategory { Name = $"Backend {Guid.NewGuid():N}" };
        fx.Db.JobCategories.Add(category);
        await fx.Db.SaveChangesAsync();

        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = title,
            Description = "Line1\nLine2",
            SalaryMin = 1000,
            SalaryMax = 2000,
            JobType = JobType.FullTime,
            ExpiryDate = expiryDate,
            Status = status,
            JobSkills = new List<JobSkill> { new() { SkillId = skill.Id, ImportanceWeight = 8 } },
            CategoryMappings = new List<JobCategoryMapping> { new() { JobCategoryId = category.Id } }
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        return job.Id;
    }

    private static JobService CreateService(TestFixture fx) =>
        new(fx.Db, new JobPostingRepository(fx.Db), new EntitlementService(fx.Db, TimeProvider.System), new StubJobAlertService(), NullLogger<JobService>.Instance);

    [Fact]
    public async Task GetPublishedAsync_ReturnsDetail_ForPublishedJob()
    {
        await using var fx = await TestFixture.CreateAsync();
        var jobId = await SeedJobAsync(fx, JobStatus.Published, DateTime.UtcNow.AddDays(7));

        var result = await CreateService(fx).GetPublishedAsync(jobId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("Senior .NET Developer", result.Data!.Title);
        Assert.Equal("Ho Chi Minh", result.Data.CompanyLocation);
        Assert.Single(result.Data.Skills);
        Assert.Equal(8, result.Data.Skills[0].ImportanceWeight);
        Assert.Single(result.Data.Categories);
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsNotFound_ForDraftJob()
    {
        await using var fx = await TestFixture.CreateAsync();
        var jobId = await SeedJobAsync(fx, JobStatus.Draft, null);

        var result = await CreateService(fx).GetPublishedAsync(jobId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsNotFound_ForExpiredJob()
    {
        await using var fx = await TestFixture.CreateAsync();
        var jobId = await SeedJobAsync(fx, JobStatus.Published, DateTime.UtcNow.AddDays(-1));

        var result = await CreateService(fx).GetPublishedAsync(jobId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsNotFound_ForMissingId()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).GetPublishedAsync(int.MaxValue);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
