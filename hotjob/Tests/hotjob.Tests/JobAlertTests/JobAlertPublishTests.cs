using hotjob.DTOs.Job;
using hotjob.Models.Job;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.JobAlertTests;

/// <summary>
/// The publish hook fires exactly when a job actually becomes Published - which, with the
/// two-step approval flow, only happens after an admin approved it - and never blocks the
/// publish when alert generation fails (contract D11).
/// </summary>
public sealed class JobAlertPublishTests
{
    private static JobService CreateService(
        TestFixture fx,
        StubJobAlertService alerts,
        ILogger<JobService>? logger = null) =>
        new(
            fx.Db,
            new JobPostingRepository(fx.Db),
            new EntitlementService(fx.Db, TimeProvider.System),
            alerts,
            logger ?? NullLogger<JobService>.Instance);

    private static async Task<(string UserId, int SkillId, int CategoryId)> SeedEmployerAsync(
        TestFixture fx,
        string email = "alerts-owner@example.com")
    {
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var employer = await MatchTestData.CreateEmployerAsync(fx, company.Id, email);
        var skill = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var category = new JobCategory { Name = $"Backend {Guid.NewGuid():N}" };
        fx.Db.JobCategories.Add(category);
        await fx.Db.SaveChangesAsync();
        return (employer.UserId, skill.Id, category.Id);
    }

    private static CreateJobRequest NewRequest(int skillId, int categoryId, JobStatus? status = null) => new()
    {
        Title = "Senior .NET Developer",
        Description = "Build things.",
        SalaryMin = 1000,
        SalaryMax = 2000,
        ExperienceRequired = 3,
        JobType = JobType.FullTime,
        Status = status,
        Skills = new List<JobSkillInput> { new() { SkillId = skillId, ImportanceWeight = 7 } },
        CategoryIds = new List<int> { categoryId }
    };

    private static UpdateJobRequest UpdateRequest(
        CreateJobRequest created,
        JobStatus status,
        string title = "Senior .NET Developer") => new()
        {
            Title = title,
            Description = created.Description,
            SalaryMin = created.SalaryMin,
            SalaryMax = created.SalaryMax,
            ExperienceRequired = created.ExperienceRequired,
            JobType = created.JobType,
            Status = status,
            Skills = created.Skills,
            CategoryIds = created.CategoryIds
        };

    [Fact]
    public async Task DraftCreate_DoesNotNotify()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var alerts = new StubJobAlertService();

        var result = await CreateService(fx, alerts).CreateAsync(
            userId, NewRequest(skillId, categoryId, JobStatus.Draft));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(JobStatus.Draft, result.Data!.Status);
        Assert.Empty(alerts.PublishedJobIds);
    }

    /// <summary>Creates a job, has an admin approve it, then publishes it (the full two-step flow).</summary>
    private static async Task<JobDetailResponse> CreateApprovedAndPublishedAsync(
        TestFixture fx, JobService service, string userId, CreateJobRequest create)
    {
        var created = await service.CreateAsync(userId, create);
        Assert.True(created.Success, string.Join("; ", created.Errors));
        await fx.ApproveJobAsync(created.Data!.Id);

        var published = await service.UpdateAsync(userId, created.Data.Id, UpdateRequest(create, JobStatus.Published));
        Assert.True(published.Success, string.Join("; ", published.Errors));
        Assert.Equal(JobStatus.Published, published.Data!.Status);
        return published.Data;
    }

    [Fact]
    public async Task PublishedCreate_GoesToReview_AndDoesNotNotifyUntilPublished()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var alerts = new StubJobAlertService();
        var service = CreateService(fx, alerts);
        var create = NewRequest(skillId, categoryId, JobStatus.Published);

        var created = await service.CreateAsync(userId, create);
        Assert.True(created.Success, string.Join("; ", created.Errors));
        Assert.Equal(JobStatus.PendingApproval, created.Data!.Status);
        Assert.Empty(alerts.PublishedJobIds);

        await fx.ApproveJobAsync(created.Data.Id);
        Assert.Empty(alerts.PublishedJobIds);

        var published = await service.UpdateAsync(userId, created.Data.Id, UpdateRequest(create, JobStatus.Published));
        Assert.True(published.Success, string.Join("; ", published.Errors));
        Assert.Equal(JobStatus.Published, published.Data!.Status);
        Assert.Equal(new[] { created.Data.Id }, alerts.PublishedJobIds);
    }

    [Fact]
    public async Task EditingAPublishedJob_DoesNotNotifyAgain()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var alerts = new StubJobAlertService();
        var service = CreateService(fx, alerts);
        var create = NewRequest(skillId, categoryId, JobStatus.Published);
        var job = await CreateApprovedAndPublishedAsync(fx, service, userId, create);

        var updated = await service.UpdateAsync(
            userId, job.Id, UpdateRequest(create, JobStatus.Published, "Lead .NET Developer"));

        Assert.True(updated.Success, string.Join("; ", updated.Errors));
        Assert.Equal(JobStatus.Published, updated.Data!.Status);
        Assert.Equal(new[] { job.Id }, alerts.PublishedJobIds);
    }

    [Fact]
    public async Task DraftToPublished_SubmitsForReview_AndNotifiesOnceApprovedAndPublished()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var alerts = new StubJobAlertService();
        var service = CreateService(fx, alerts);
        var create = NewRequest(skillId, categoryId, JobStatus.Draft);
        var created = await service.CreateAsync(userId, create);
        Assert.True(created.Success, string.Join("; ", created.Errors));

        var submitted = await service.UpdateAsync(userId, created.Data!.Id, UpdateRequest(create, JobStatus.Published));
        Assert.True(submitted.Success, string.Join("; ", submitted.Errors));
        Assert.Equal(JobStatus.PendingApproval, submitted.Data!.Status);
        Assert.NotNull(submitted.Data.SubmittedForApprovalAt);
        Assert.Empty(alerts.PublishedJobIds);

        await fx.ApproveJobAsync(created.Data.Id);
        var published = await service.UpdateAsync(userId, created.Data.Id, UpdateRequest(create, JobStatus.Published));

        Assert.True(published.Success, string.Join("; ", published.Errors));
        Assert.Equal(JobStatus.Published, published.Data!.Status);
        Assert.Equal(new[] { created.Data.Id }, alerts.PublishedJobIds);
    }

    [Fact]
    public async Task ReopeningAClosedJob_GoesBackToReview_AndNotifiesAgainWhenRepublished()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var alerts = new StubJobAlertService();
        var service = CreateService(fx, alerts);
        var create = NewRequest(skillId, categoryId, JobStatus.Published);
        var job = await CreateApprovedAndPublishedAsync(fx, service, userId, create);

        var closed = await service.DeleteOrCloseAsync(userId, job.Id);
        Assert.True(closed.Success, string.Join("; ", closed.Errors));

        var reopened = await service.UpdateAsync(userId, job.Id, UpdateRequest(create, JobStatus.Published));
        Assert.True(reopened.Success, string.Join("; ", reopened.Errors));
        Assert.Equal(JobStatus.PendingApproval, reopened.Data!.Status);
        Assert.Null(reopened.Data.ReviewedAt);
        Assert.Single(alerts.PublishedJobIds);

        await fx.ApproveJobAsync(job.Id);
        var republished = await service.UpdateAsync(userId, job.Id, UpdateRequest(create, JobStatus.Published));

        Assert.True(republished.Success, string.Join("; ", republished.Errors));
        Assert.Equal(JobStatus.Published, republished.Data!.Status);
        Assert.Equal(new[] { job.Id, job.Id }, alerts.PublishedJobIds);
    }

    [Fact]
    public async Task RejectedJob_CanBeResubmittedForReview()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var alerts = new StubJobAlertService();
        var service = CreateService(fx, alerts);
        var create = NewRequest(skillId, categoryId, JobStatus.Published);
        var created = await service.CreateAsync(userId, create);
        await fx.RejectJobAsync(created.Data!.Id, "Salary missing");

        var resubmitted = await service.UpdateAsync(userId, created.Data.Id, UpdateRequest(create, JobStatus.Published));

        Assert.True(resubmitted.Success, string.Join("; ", resubmitted.Errors));
        Assert.Equal(JobStatus.PendingApproval, resubmitted.Data!.Status);
        Assert.Null(resubmitted.Data.RejectionReason);
        Assert.Empty(alerts.PublishedJobIds);
    }

    [Fact]
    public async Task AlertFailure_DoesNotFailPublish_AndIsLoggedWithTheJob()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var failure = new InvalidOperationException("alert backend unavailable");
        var alerts = new StubJobAlertService { PublishFailure = failure };
        var logger = new CapturingLogger<JobService>();
        var service = CreateService(fx, alerts, logger);

        var job = await CreateApprovedAndPublishedAsync(
            fx, service, userId, NewRequest(skillId, categoryId, JobStatus.Published));

        Assert.Equal(
            JobStatus.Published,
            await fx.Db.JobPostings
                .Where(posting => posting.Id == job.Id)
                .Select(posting => posting.Status)
                .SingleAsync());

        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(failure, error.Exception);
        Assert.Contains("published job", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(job.Id.ToString(), error.Message);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
