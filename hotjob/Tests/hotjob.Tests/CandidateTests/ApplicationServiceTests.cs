using hotjob.Data;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Models;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.CandidateTests;

/// <summary>
/// Apply/withdraw rules: ownership, job state, duplicate guard, snapshot freeze,
/// history and employer notifications, and terminal-status protection.
/// </summary>
public sealed class ApplicationServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private static ApplicationService NewService(TestFixture fx, FakeTimeProvider clock) =>
        new(fx.Db, clock, NullLogger<ApplicationService>.Instance);

    private sealed record JobHarness(
        int CompanyId,
        int JobId,
        string JobTitle,
        IReadOnlyList<string> ActiveEmployerUserIds,
        string PendingEmployerUserId,
        string OtherCompanyEmployerUserId);

    private static async Task<JobHarness> SeedJobAsync(
        TestFixture fx,
        JobStatus status = JobStatus.Published,
        DateTime? expiry = null,
        string title = "Backend Engineer")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var company = new CompanyProfile
        {
            CompanyName = $"Co {suffix}",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var activeIds = new List<string>();
        for (var index = 0; index < 2; index++)
        {
            var user = await fx.SeedUserAsync($"active{index}-{suffix}@example.com", "Str0ng!Pass", AppRoles.Employer);
            fx.Db.Employers.Add(new Employer
            {
                UserId = user.Id,
                CompanyProfileId = company.Id,
                FullName = $"Active {index}",
                MembershipStatus = MembershipStatus.Active,
                IsOwner = index == 0
            });
            activeIds.Add(user.Id);
        }

        var pending = await fx.SeedUserAsync($"pending-{suffix}@example.com", "Str0ng!Pass", AppRoles.Employer);
        fx.Db.Employers.Add(new Employer
        {
            UserId = pending.Id,
            CompanyProfileId = company.Id,
            FullName = "Pending",
            MembershipStatus = MembershipStatus.Pending
        });

        var otherCompany = new CompanyProfile
        {
            CompanyName = $"Other {suffix}",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(otherCompany);
        var otherUser = await fx.SeedUserAsync($"other-{suffix}@example.com", "Str0ng!Pass", AppRoles.Employer);
        fx.Db.Employers.Add(new Employer
        {
            UserId = otherUser.Id,
            CompanyProfileId = otherCompany.Id,
            FullName = "Other",
            MembershipStatus = MembershipStatus.Active
        });
        await fx.Db.SaveChangesAsync();

        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = title,
            Description = "Build APIs.",
            Status = status,
            ExpiryDate = expiry
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();

        return new JobHarness(company.Id, job.Id, title, activeIds, pending.Id, otherUser.Id);
    }

    private static async Task<(string UserId, int CandidateId)> SeedCandidateAsync(
        TestFixture fx,
        string email = "candidate@example.com",
        string fullName = "Lan Nguyen")
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = fullName };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();
        return (user.Id, candidate.Id);
    }

    private static async Task<int> SeedResumeAsync(
        TestFixture fx,
        int candidateId,
        string title = "Backend CV",
        string? summary = "3 years .NET + SQL.",
        string? fileUrl = "/uploads/cv/1/cv.pdf",
        bool isPrimary = true)
    {
        var resume = new Resume
        {
            CandidateId = candidateId,
            Title = title,
            Summary = summary,
            FileUrl = fileUrl,
            IsPrimary = isPrimary,
            CreatedAt = Start.UtcDateTime
        };
        fx.Db.Resumes.Add(resume);
        await fx.Db.SaveChangesAsync();
        return resume.Id;
    }

    private static async Task<int> SeedApplicationAsync(
        TestFixture fx,
        int jobId,
        int resumeId,
        int candidateId,
        ApplicationStatus status,
        DateTime? appliedAt = null)
    {
        var application = new Application
        {
            JobPostingId = jobId,
            ResumeId = resumeId,
            CandidateId = candidateId,
            CurrentStatus = status,
            AppliedAt = appliedAt ?? Start.UtcDateTime
        };
        fx.Db.Applications.Add(application);
        await fx.Db.SaveChangesAsync();
        return application.Id;
    }

    [Fact]
    public async Task ApplyAsync_Success_FreezesSnapshotWritesHistoryAndNotifiesActiveEmployers()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx);

        var result = await NewService(fx, clock).ApplyAsync(
            userId, job.JobId, resumeId, "  I would love to join.  ");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(ApplicationStatus.Pending, result.Data!.Status);
        Assert.Equal(Start.UtcDateTime, result.Data.AppliedAt);

        var application = await fx.Db.Applications.AsNoTracking().SingleAsync();
        Assert.Equal(job.JobId, application.JobPostingId);
        Assert.Equal(resumeId, application.ResumeId);
        Assert.Equal(candidateId, application.CandidateId);
        Assert.Equal(ApplicationStatus.Pending, application.CurrentStatus);
        Assert.Equal("3 years .NET + SQL.", application.SnapshotSummary);
        Assert.Equal("/uploads/cv/1/cv.pdf", application.SnapshotFileUrl);
        Assert.Equal("I would love to join.", application.CoverLetter);
        Assert.Equal(Start.UtcDateTime, application.AppliedAt);

        var history = await fx.Db.ApplicationStatusHistories.AsNoTracking().SingleAsync();
        Assert.Equal(application.Id, history.ApplicationId);
        Assert.Equal(ApplicationStatus.Pending, history.Status);
        Assert.Equal(userId, history.ChangedByUserId);
        Assert.Equal("Application submitted", history.Notes);
        Assert.Equal(Start.UtcDateTime, history.ChangedAt);

        var notifications = await fx.Db.Notifications.AsNoTracking().ToListAsync();
        Assert.Equal(job.ActiveEmployerUserIds.Count, notifications.Count);
        Assert.All(notifications, notification =>
        {
            Assert.Contains(notification.UserId, job.ActiveEmployerUserIds);
            Assert.Contains(job.JobTitle, notification.Title);
            Assert.Contains("Lan Nguyen", notification.Content);
            Assert.Contains("Backend CV", notification.Content);
            Assert.False(notification.IsRead);
            Assert.Equal(Start.UtcDateTime, notification.CreatedAt);
        });
        Assert.DoesNotContain(notifications, notification => notification.UserId == job.PendingEmployerUserId);
        Assert.DoesNotContain(notifications, notification => notification.UserId == job.OtherCompanyEmployerUserId);
        Assert.DoesNotContain(notifications, notification => notification.UserId == userId);
    }

    [Fact]
    public async Task ApplyAsync_Duplicate_ReturnsConflictAndCreatesNothingElse()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx);
        var service = NewService(fx, clock);
        Assert.True((await service.ApplyAsync(userId, job.JobId, resumeId, null)).Success);

        var second = await service.ApplyAsync(userId, job.JobId, resumeId, "again");

        Assert.False(second.Success);
        Assert.Equal(ServiceErrorType.Conflict, second.ErrorType);
        Assert.Contains("already applied", Assert.Single(second.Errors));
        Assert.Equal(1, await fx.Db.Applications.CountAsync());
        Assert.Equal(1, await fx.Db.ApplicationStatusHistories.CountAsync());
        Assert.Equal(job.ActiveEmployerUserIds.Count, await fx.Db.Notifications.CountAsync());
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Expired)]
    public async Task ApplyAsync_UnpublishedJob_ReturnsConflictWithoutApplication(JobStatus status)
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx, status);

        var result = await NewService(fx, clock).ApplyAsync(userId, job.JobId, resumeId, null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Contains("not accepting applications", string.Join(" ", result.Errors));
        Assert.Equal(0, await fx.Db.Applications.CountAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task ApplyAsync_ExpiredJob_ReturnsConflictWithoutApplication(int minutesPastExpiry)
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx, expiry: Start.UtcDateTime.AddMinutes(minutesPastExpiry));

        var result = await NewService(fx, clock).ApplyAsync(userId, job.JobId, resumeId, null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Contains("no longer accepting", string.Join(" ", result.Errors));
        Assert.Equal(0, await fx.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task ApplyAsync_JobWithFutureExpiry_IsAccepted()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx, expiry: Start.UtcDateTime.AddDays(1));

        var result = await NewService(fx, clock).ApplyAsync(userId, job.JobId, resumeId, null);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task ApplyAsync_ResumeFromAnotherCandidate_IsNotFoundAndCreatesNothing()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, _) = await SeedCandidateAsync(fx);
        var (_, otherCandidateId) = await SeedCandidateAsync(fx, "other@example.com", "Other Person");
        var otherResumeId = await SeedResumeAsync(fx, otherCandidateId, "Other CV");
        var job = await SeedJobAsync(fx);

        var result = await NewService(fx, clock).ApplyAsync(userId, job.JobId, otherResumeId, null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
        Assert.Equal(0, await fx.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task ApplyAsync_AnonymousUser_IsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (_, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx);

        var result = await NewService(fx, clock).ApplyAsync(null, job.JobId, resumeId, null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        Assert.Equal(0, await fx.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task ApplyAsync_UserWithoutCandidateProfile_IsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var ghost = await fx.SeedUserAsync("ghost@example.com", "Str0ng!Pass", AppRoles.Candidate);
        var job = await SeedJobAsync(fx);

        var result = await NewService(fx, clock).ApplyAsync(ghost.Id, job.JobId, 1, null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
        Assert.Contains("Candidate profile", string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task ApplyAsync_UnknownJob_IsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);

        var result = await NewService(fx, clock).ApplyAsync(userId, 9999, resumeId, null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task ApplyAsync_CoverLetterOverLimit_IsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx);

        var result = await NewService(fx, clock).ApplyAsync(
            userId, job.JobId, resumeId, new string('x', 2001));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Equal(0, await fx.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task GetApplyContextAsync_ReturnsPrimaryFirstAndAlreadyAppliedState()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var primaryId = await SeedResumeAsync(fx, candidateId, "Primary CV", isPrimary: true);
        await SeedResumeAsync(fx, candidateId, "Secondary CV", isPrimary: false);
        var job = await SeedJobAsync(fx);
        var applicationId = await SeedApplicationAsync(
            fx, job.JobId, primaryId, candidateId, ApplicationStatus.Viewed);

        var result = await NewService(fx, clock).GetApplyContextAsync(userId, job.JobId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.AlreadyApplied);
        Assert.Equal(applicationId, result.Data.ApplicationId);
        Assert.Equal(ApplicationStatus.Viewed, result.Data.CurrentStatus);
        Assert.Equal(2, result.Data.Resumes.Count);
        Assert.Equal(primaryId, result.Data.Resumes[0].Id);
        Assert.True(result.Data.Resumes[0].IsPrimary);
    }

    [Theory]
    [InlineData(ApplicationStatus.Pending)]
    [InlineData(ApplicationStatus.Viewed)]
    [InlineData(ApplicationStatus.Shortlisted)]
    [InlineData(ApplicationStatus.Interviewing)]
    public async Task WithdrawAsync_InProgressStatus_SucceedsAndWritesHistory(ApplicationStatus current)
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx);
        var applicationId = await SeedApplicationAsync(fx, job.JobId, resumeId, candidateId, current);

        var result = await NewService(fx, clock).WithdrawAsync(userId, applicationId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var stored = await fx.Db.Applications.AsNoTracking().SingleAsync(row => row.Id == applicationId);
        Assert.Equal(ApplicationStatus.Withdrawn, stored.CurrentStatus);

        var history = await fx.Db.ApplicationStatusHistories.AsNoTracking()
            .SingleAsync(entry => entry.ApplicationId == applicationId);
        Assert.Equal(ApplicationStatus.Withdrawn, history.Status);
        Assert.Equal(userId, history.ChangedByUserId);
        Assert.Equal("Withdrawn by candidate", history.Notes);
        Assert.Equal(Start.UtcDateTime, history.ChangedAt);
    }

    [Theory]
    [InlineData(ApplicationStatus.Offered)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public async Task WithdrawAsync_TerminalStatus_ReturnsConflictAndWritesNothing(ApplicationStatus current)
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx);
        var applicationId = await SeedApplicationAsync(fx, job.JobId, resumeId, candidateId, current);

        var result = await NewService(fx, clock).WithdrawAsync(userId, applicationId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Contains("can no longer be withdrawn", string.Join(" ", result.Errors));
        var stored = await fx.Db.Applications.AsNoTracking().SingleAsync(row => row.Id == applicationId);
        Assert.Equal(current, stored.CurrentStatus);
        Assert.Equal(0, await fx.Db.ApplicationStatusHistories.CountAsync());
    }

    [Fact]
    public async Task WithdrawAsync_AnotherCandidatesApplication_IsNotFoundAndUnchanged()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (userId, _) = await SeedCandidateAsync(fx);
        var (_, otherCandidateId) = await SeedCandidateAsync(fx, "other@example.com", "Other Person");
        var resumeId = await SeedResumeAsync(fx, otherCandidateId, "Other CV");
        var job = await SeedJobAsync(fx);
        var applicationId = await SeedApplicationAsync(
            fx, job.JobId, resumeId, otherCandidateId, ApplicationStatus.Shortlisted);

        var result = await NewService(fx, clock).WithdrawAsync(userId, applicationId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
        var stored = await fx.Db.Applications.AsNoTracking().SingleAsync(row => row.Id == applicationId);
        Assert.Equal(ApplicationStatus.Shortlisted, stored.CurrentStatus);
        Assert.Equal(0, await fx.Db.ApplicationStatusHistories.CountAsync());
    }

    [Fact]
    public async Task WithdrawAsync_AnonymousUser_IsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        var (_, candidateId) = await SeedCandidateAsync(fx);
        var resumeId = await SeedResumeAsync(fx, candidateId);
        var job = await SeedJobAsync(fx);
        var applicationId = await SeedApplicationAsync(
            fx, job.JobId, resumeId, candidateId, ApplicationStatus.Pending);

        var result = await NewService(fx, clock).WithdrawAsync(null, applicationId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }
}
