using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.JobTests;

public sealed class SavedJobTests
{
    private static JobService CreateService(TestFixture fx) =>
        new(
            fx.Db,
            new JobPostingRepository(fx.Db),
            new EntitlementService(fx.Db, TimeProvider.System),
            new StubJobAlertService(),
            NullLogger<JobService>.Instance, TimeProvider.System);

    private static async Task<Candidate> SeedCandidateAsync(TestFixture fx) =>
        (await MatchTestData.CreateCandidateAsync(fx, $"{Guid.NewGuid():N}@example.com")).Candidate;

    private static async Task<int> SeedJobAsync(TestFixture fx, JobStatus status = JobStatus.Published)
    {
        var company = new CompanyProfile
        {
            CompanyName = $"Saved Co {Guid.NewGuid():N}",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Saved role",
            Description = "Description",
            Status = status,
            JobType = JobType.FullTime
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task SaveThenListThenUnsave_RoundTrips()
    {
        await using var fx = await TestFixture.CreateAsync();
        var candidate = await SeedCandidateAsync(fx);
        var jobId = await SeedJobAsync(fx);
        var service = CreateService(fx);

        var saved = await service.SaveJobAsync(candidate.UserId, jobId);
        Assert.True(saved.Success);
        Assert.True((await service.IsSavedAsync(candidate.UserId, jobId)).Data);

        var list = await service.GetSavedJobsAsync(candidate.UserId, 1, 20);
        var item = Assert.Single(list.Data!.Items);
        Assert.Equal(jobId, item.JobId);
        Assert.Equal("Saved role", item.Title);
        Assert.Equal(1, list.Data.TotalCount);

        var duplicate = await service.SaveJobAsync(candidate.UserId, jobId);
        Assert.True(duplicate.Success);
        Assert.Equal(1, await fx.Db.SavedJobs.CountAsync());

        var removed = await service.UnsaveJobAsync(candidate.UserId, jobId);
        Assert.True(removed.Success);
        Assert.False((await service.IsSavedAsync(candidate.UserId, jobId)).Data);
    }

    [Fact]
    public async Task SaveDraftJob_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var candidate = await SeedCandidateAsync(fx);
        var jobId = await SeedJobAsync(fx, JobStatus.Draft);

        var result = await CreateService(fx).SaveJobAsync(candidate.UserId, jobId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetSavedJobs_NonCandidate_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).GetSavedJobsAsync("missing-user", 1, 20);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }
}
