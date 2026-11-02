using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.ML;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.JobTests;

public sealed class JobSkillMatchTests
{
    private static JobService CreateService(TestFixture fx) =>
        new(
            fx.Db,
            new JobPostingRepository(fx.Db),
            new EntitlementService(fx.Db, TimeProvider.System),
            new StubJobAlertService(),
            NullLogger<JobService>.Instance);

    private static async Task<(string UserId, int ResumeId)> SeedCandidateWithSkillsAsync(
        TestFixture fx,
        params string[] skillNames)
    {
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(
            fx,
            $"{Guid.NewGuid():N}@example.com",
            skillNames.Select(name => (name, 1)).ToArray());

        return (candidate.UserId, resume.Id);
    }

    private static async Task<int> SeedJobAsync(
        TestFixture fx,
        string title,
        string[] skillNames,
        int? resumeId = null,
        double? score = null,
        string city = "Hanoi",
        JobType jobType = JobType.FullTime,
        DateTime? expiryDate = null,
        CompanyStatus companyStatus = CompanyStatus.Approved)
    {
        var company = new CompanyProfile
        {
            CompanyName = $"Company {Guid.NewGuid():N}",
            Location = city,
            Status = companyStatus
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = title,
            Description = "Build things",
            JobType = jobType,
            ExpiryDate = expiryDate,
            Status = JobStatus.Published,
            CreatedAt = DateTime.UtcNow
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();

        foreach (var skillName in skillNames)
        {
            var skill = await MatchTestData.GetOrCreateSkillAsync(fx, skillName);
            fx.Db.JobSkills.Add(new JobSkill
            {
                JobPostingId = job.Id,
                SkillId = skill.Id
            });
        }

        if (resumeId.HasValue && score.HasValue)
        {
            fx.Db.MatchScores.Add(new MatchScore
            {
                JobPostingId = job.Id,
                ResumeId = resumeId.Value,
                MatchPercentage = score.Value
            });
        }

        await fx.Db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_ReturnsMatchingJobsWithSkills()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#", "SQL");
        var jobId = await SeedJobAsync(
            fx, "Senior .NET", ["C#", "SQL"], resumeId: resumeId, score: 80);

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, null, null, 1, 1, 20);

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(jobId, item.Id);
        Assert.Equal(2, item.MatchedSkillCount);
        Assert.Equal(new[] { "C#", "SQL" }, item.MatchedSkills.Select(skill => skill.SkillName));
        Assert.Equal(80d, item.MatchPercentage!.Value);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_ResumeWithNoSkills_ReturnsEmpty()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx);

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, null, null, 1, 1, 20);

        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.TotalCount);
        Assert.Empty(result.Data.Items);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_MinSkillMatchesFilters()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#", "SQL");
        await SeedJobAsync(fx, "One match", ["C#"]);

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, null, null, 2, 1, 20);

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Items);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_SortsByMatchedCountThenScore()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#", "SQL");
        await SeedJobAsync(fx, "A", ["C#", "SQL"], resumeId: resumeId, score: 50);
        await SeedJobAsync(fx, "B", ["C#", "SQL"], resumeId: resumeId, score: 90);
        await SeedJobAsync(fx, "C", ["C#"]);

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, null, null, 1, 1, 20);

        Assert.True(result.Success);
        Assert.Equal(new[] { "B", "A", "C" }, result.Data!.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_OtherUserResume_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (_, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#");

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            "other-user", resumeId, null, null, 1, 1, 20);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_MissingResume_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateWithSkillsAsync(fx, "C#");

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, 9999, null, null, 1, 1, 20);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_ExpiredJobExcluded()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#");
        await SeedJobAsync(
            fx, "Expired", ["C#"], expiryDate: DateTime.UtcNow.AddDays(-1));

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, null, null, 1, 1, 20);

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Items);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_PendingCompanyExcluded()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#");
        await SeedJobAsync(
            fx, "Pending", ["C#"], companyStatus: CompanyStatus.Pending);

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, null, null, 1, 1, 20);

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Items);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_NoMatchScore_ReturnsNullPercentage()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#");
        await SeedJobAsync(fx, "No score", ["C#"]);

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, null, null, 1, 1, 20);

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Null(item.MatchPercentage);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_CityAndJobTypeFilters()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#");
        await SeedJobAsync(fx, "Hanoi full time", ["C#"], city: "Hanoi", jobType: JobType.FullTime);
        await SeedJobAsync(fx, "Da Nang part time", ["C#"], city: "Da Nang", jobType: JobType.PartTime);

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, "Ha", JobType.FullTime, 1, 1, 20);

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Hanoi full time", item.Title);
    }

    [Fact]
    public async Task GetJobsMatchingResumeAsync_ClampsPageAndMinMatches()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, resumeId) = await SeedCandidateWithSkillsAsync(fx, "C#");
        await SeedJobAsync(fx, "One match", ["C#"]);

        var result = await CreateService(fx).GetJobsMatchingResumeAsync(
            userId, resumeId, null, null, 0, 0, 500);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(100, result.Data.PageSize);
        Assert.Single(result.Data.Items);
    }
}
