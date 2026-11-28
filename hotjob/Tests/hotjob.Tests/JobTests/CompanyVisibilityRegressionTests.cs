using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.DTOs.ML;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Repositories.Employer;
using hotjob.Repositories.Job;
using hotjob.Services.Admin;
using hotjob.Services.Billing;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using hotjob.Services.Job;
using hotjob.Services.JobAlerts;
using hotjob.Services.ML;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.JobTests;

/// <summary>
/// Guards the deliberately untouched employer/admin/history flows and proves that
/// company re-approval restores candidate visibility without mutating any row.
/// </summary>
public sealed class CompanyVisibilityRegressionTests
{
    private static JobService CreateJobService(TestFixture fx) => new(
        fx.Db,
        new JobPostingRepository(fx.Db),
        new EntitlementService(fx.Db, TimeProvider.System),
        new StubJobAlertService(),
        NullLogger<JobService>.Instance,
        TimeProvider.System);

    private static CompanyProfile AddCompany(TestFixture fx, CompanyStatus status) =>
        new() { CompanyName = $"Visibility Co {Guid.NewGuid():N}", Status = status };

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task OwnedQueries_StillReturnDraftAndPublishedJobs_ForNonApprovedCompanies(
        CompanyStatus companyStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = AddCompany(fx, companyStatus);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var draft = new JobPosting
        {
            CompanyProfileId = company.Id, Title = "Draft job", Description = "d", Status = JobStatus.Draft
        };
        var published = new JobPosting
        {
            CompanyProfileId = company.Id, Title = "Published job", Description = "d", Status = JobStatus.Published
        };
        fx.Db.JobPostings.AddRange(draft, published);
        await fx.Db.SaveChangesAsync();

        var repository = new JobPostingRepository(fx.Db);
        var (items, total) = await repository.ListOwnedAsync(company.Id, null, null, 1, 20);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, job => job.Id == draft.Id && job.Status == JobStatus.Draft);
        Assert.Contains(items, job => job.Id == published.Id && job.Status == JobStatus.Published);

        var draftDetail = await repository.GetByIdOwnedAsync(draft.Id, company.Id, includeDetails: false);
        var publishedDetail = await repository.GetByIdOwnedAsync(published.Id, company.Id, includeDetails: true);
        Assert.NotNull(draftDetail);
        Assert.Equal("Draft job", draftDetail!.Title);
        Assert.NotNull(publishedDetail);
        Assert.Equal("Published job", publishedDetail!.Title);
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task AdminJobList_StillReturnsJobsOfNonApprovedCompany(CompanyStatus companyStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = AddCompany(fx, companyStatus);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var job = new JobPosting
        {
            CompanyProfileId = company.Id, Title = "Admin visible", Description = "d", Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();

        var result = await fx.CreateJobAdminService().ListAsync(1, 20);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(result.Data!.Items, item => item.Id == job.Id && item.Title == "Admin visible");
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task EmployerGuard_StillRevokesNonApprovedCompany(CompanyStatus companyStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = AddCompany(fx, companyStatus);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var employerUser = await fx.SeedUserAsync(
            $"guard-{companyStatus}-{Guid.NewGuid():N}@example.com", "Str0ng!Pass", AppRoles.Employer);
        fx.Db.Employers.Add(new Employer
        {
            UserId = employerUser.Id,
            CompanyProfileId = company.Id,
            FullName = "Active HR",
            MembershipStatus = MembershipStatus.Active,
            IsOwner = true
        });
        var candidateUser = await fx.SeedUserAsync(
            $"cand-{Guid.NewGuid():N}@example.com", "Str0ng!Pass", AppRoles.Candidate);
        var candidate = new Candidate { UserId = candidateUser.Id, FullName = "Cand" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();
        var resume = new Resume { CandidateId = candidate.Id, Title = "CV" };
        fx.Db.Resumes.Add(resume);
        var job = new JobPosting
        {
            CompanyProfileId = company.Id, Title = "ATS job", Description = "d", Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        var application = new Application
        {
            JobPostingId = job.Id, ResumeId = resume.Id, CandidateId = candidate.Id,
            CurrentStatus = ApplicationStatus.Pending, AppliedAt = DateTime.UtcNow
        };
        fx.Db.Applications.Add(application);
        await fx.Db.SaveChangesAsync();

        var service = new ApplicantService(
            new ApplicantRepository(fx.Db), NullLogger<ApplicantService>.Instance);
        var result = await service.GetDetailAsync(employerUser.Id, application.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task ExistingApplication_FromLaterRejectedCompany_RemainsVisibleAndWithdrawable()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = AddCompany(fx, CompanyStatus.Approved);
        company.CompanyName = "Rejected Employer";
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var user = await fx.SeedUserAsync($"history-{Guid.NewGuid():N}@example.com", "Str0ng!Pass", AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = "History Cand" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();
        var resume = new Resume { CandidateId = candidate.Id, Title = "Frozen CV" };
        fx.Db.Resumes.Add(resume);
        var job = new JobPosting
        {
            CompanyProfileId = company.Id, Title = "History role", Description = "d", Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();

        const string snapshotJson = "{\"Title\":\"Frozen CV\"}";
        const string snapshotSummary = "Frozen summary";
        const string snapshotFileUrl = "/uploads/cv/frozen.pdf";
        var application = new Application
        {
            JobPostingId = job.Id,
            ResumeId = resume.Id,
            CandidateId = candidate.Id,
            CurrentStatus = ApplicationStatus.Viewed,
            AppliedAt = DateTime.UtcNow.AddDays(-2),
            SnapshotJson = snapshotJson,
            SnapshotSummary = snapshotSummary,
            SnapshotFileUrl = snapshotFileUrl
        };
        fx.Db.Applications.Add(application);
        await fx.Db.SaveChangesAsync();

        company.Status = CompanyStatus.Rejected;
        await fx.Db.SaveChangesAsync();

        var candidateService = new CandidateService(fx.Db);
        var list = await candidateService.GetMyApplicationsAsync(user.Id);
        var page = await candidateService.GetMyApplicationsPageAsync(user.Id, new MyApplicationsQuery());

        Assert.True(list.Success, string.Join("; ", list.Errors));
        var fromList = Assert.Single(list.Data!);
        Assert.Equal("History role", fromList.JobTitle);
        Assert.Equal("Rejected Employer", fromList.CompanyName);
        Assert.Equal(ApplicationStatus.Viewed, fromList.Status);

        Assert.True(page.Success, string.Join("; ", page.Errors));
        var fromPage = Assert.Single(page.Data!.Items);
        Assert.Equal("History role", fromPage.JobTitle);
        Assert.Equal("Rejected Employer", fromPage.CompanyName);
        Assert.Equal(ApplicationStatus.Viewed, fromPage.Status);

        var withdraw = await new ApplicationService(
            fx.Db, TimeProvider.System, NullLogger<ApplicationService>.Instance)
            .WithdrawAsync(user.Id, application.Id);

        Assert.True(withdraw.Success, string.Join("; ", withdraw.Errors));
        var stored = await fx.Db.Applications.SingleAsync(row => row.Id == application.Id);
        Assert.Equal(ApplicationStatus.Withdrawn, stored.CurrentStatus);
        Assert.Equal(snapshotJson, stored.SnapshotJson);
        Assert.Equal(snapshotSummary, stored.SnapshotSummary);
        Assert.Equal(snapshotFileUrl, stored.SnapshotFileUrl);
        Assert.Single(await fx.Db.ApplicationStatusHistories
            .Where(entry => entry.ApplicationId == application.Id).ToListAsync());
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task Reapproval_RestoresDetailBrowseAndSavedJobs(CompanyStatus hiddenStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = AddCompany(fx, hiddenStatus);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Reapproval role",
            Description = "d",
            Status = JobStatus.Published,
            ExpiryDate = DateTime.UtcNow.AddDays(7)
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();

        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, $"saved-{Guid.NewGuid():N}@example.com");
        fx.Db.SavedJobs.Add(new SavedJob
        {
            CandidateId = candidate.Id, JobPostingId = job.Id, SavedAt = DateTime.UtcNow
        });
        await fx.Db.SaveChangesAsync();

        var service = CreateJobService(fx);
        var detailBefore = await service.GetPublishedAsync(job.Id);
        var browseBefore = await service.BrowsePublishedAsync(null, null, null, null, null, null, null, null, 1, 20);
        var savedBefore = await service.GetSavedJobsAsync(candidate.UserId, 1, 20);
        Assert.False(detailBefore.Success);
        Assert.Equal(ServiceErrorType.NotFound, detailBefore.ErrorType);
        Assert.Empty(browseBefore.Data!.Items);
        Assert.Equal(0, browseBefore.Data.TotalCount);
        Assert.Empty(savedBefore.Data!.Items);
        Assert.Equal(0, savedBefore.Data.TotalCount);

        var jobCount = await fx.Db.JobPostings.CountAsync();
        var bookmarkCount = await fx.Db.SavedJobs.CountAsync();
        company.Status = CompanyStatus.Approved;
        await fx.Db.SaveChangesAsync();

        var detailAfter = await service.GetPublishedAsync(job.Id);
        var browseAfter = await service.BrowsePublishedAsync(null, null, null, null, null, null, null, null, 1, 20);
        var savedAfter = await service.GetSavedJobsAsync(candidate.UserId, 1, 20);
        Assert.True(detailAfter.Success, string.Join("; ", detailAfter.Errors));
        Assert.Equal(job.Id, detailAfter.Data!.Id);
        Assert.Equal(job.Id, Assert.Single(browseAfter.Data!.Items).Id);
        Assert.Equal(1, browseAfter.Data.TotalCount);
        Assert.Equal(job.Id, Assert.Single(savedAfter.Data!.Items).JobId);
        Assert.Equal(1, savedAfter.Data.TotalCount);
        Assert.Equal(jobCount, await fx.Db.JobPostings.CountAsync());
        Assert.Equal(bookmarkCount, await fx.Db.SavedJobs.CountAsync());
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task Reapproval_RestoresRecommendations(CompanyStatus hiddenStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(
            fx, $"match-{Guid.NewGuid():N}@example.com", ("C#", 3));
        var skill = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var company = AddCompany(fx, hiddenStatus);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [(skill.Id, 10)], title: "Match role");

        var service = CreateJobService(fx);
        var before = await service.GetJobsMatchingResumeAsync(
            candidate.UserId, resume.Id, null, null, 1, 1, 20);
        Assert.True(before.Success);
        Assert.Empty(before.Data!.Items);
        Assert.Equal(0, before.Data.TotalCount);

        var jobCount = await fx.Db.JobPostings.CountAsync();
        var resumeCount = await fx.Db.Resumes.CountAsync();
        company.Status = CompanyStatus.Approved;
        await fx.Db.SaveChangesAsync();

        var after = await service.GetJobsMatchingResumeAsync(
            candidate.UserId, resume.Id, null, null, 1, 1, 20);
        Assert.True(after.Success, string.Join("; ", after.Errors));
        Assert.Equal(job.Id, Assert.Single(after.Data!.Items).Id);
        Assert.Equal(jobCount, await fx.Db.JobPostings.CountAsync());
        Assert.Equal(resumeCount, await fx.Db.Resumes.CountAsync());
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task Reapproval_AllowsAlertGeneration(CompanyStatus hiddenStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(
            fx, $"alert-{Guid.NewGuid():N}@example.com", ("C#", 3));
        var skill = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var company = AddCompany(fx, hiddenStatus);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [(skill.Id, 10)], title: "Alert role");

        var service = new JobAlertService(
            fx.Db,
            new StubMatchScoreService(),
            Options.Create(new JobAlertsOptions { MinMatchPercent = 60 }),
            TimeProvider.System,
            NullLogger<JobAlertService>.Instance);

        Assert.Equal(0, await service.NotifyForPublishedJobAsync(job.Id));
        Assert.Equal(0, await fx.Db.Notifications.CountAsync());

        var jobCount = await fx.Db.JobPostings.CountAsync();
        company.Status = CompanyStatus.Approved;
        await fx.Db.SaveChangesAsync();

        Assert.Equal(1, await service.NotifyForPublishedJobAsync(job.Id));
        var alert = Assert.Single(await fx.Db.Notifications.ToListAsync());
        Assert.Equal(job.Id, alert.RelatedJobPostingId);
        Assert.Equal(candidate.UserId, alert.UserId);
        Assert.Equal(jobCount, await fx.Db.JobPostings.CountAsync());
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task Reapproval_RestoresExistingAlertFeedWithSameAlert(CompanyStatus hiddenStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(
            fx, $"alert-feed-{Guid.NewGuid():N}@example.com");
        var company = AddCompany(fx, CompanyStatus.Approved);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Feed role",
            Description = "d",
            Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        var alert = new Notification
        {
            UserId = candidate.UserId,
            Kind = NotificationKinds.JobAlert,
            RelatedJobPostingId = job.Id,
            Title = "Existing alert",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        fx.Db.Notifications.Add(alert);
        await fx.Db.SaveChangesAsync();
        var alertId = alert.Id;
        var notificationCount = await fx.Db.Notifications.CountAsync();
        var jobCount = await fx.Db.JobPostings.CountAsync();

        var service = new JobAlertService(
            fx.Db,
            new StubMatchScoreService(),
            Options.Create(new JobAlertsOptions { MinMatchPercent = 60 }),
            TimeProvider.System,
            NullLogger<JobAlertService>.Instance);

        var before = await service.GetAlertsAsync(candidate.UserId, 1, 20);
        Assert.True(before.Success, string.Join("; ", before.Errors));
        Assert.Equal(1, before.Data!.TotalCount);
        Assert.Equal(1, before.Data.UnreadCount);
        Assert.Equal(alertId, Assert.Single(before.Data.Alerts).Id);

        company.Status = hiddenStatus;
        await fx.Db.SaveChangesAsync();
        var hidden = await service.GetAlertsAsync(candidate.UserId, 1, 20);
        Assert.True(hidden.Success, string.Join("; ", hidden.Errors));
        Assert.Empty(hidden.Data!.Alerts);
        Assert.Equal(0, hidden.Data.TotalCount);
        Assert.Equal(0, hidden.Data.UnreadCount);

        company.Status = CompanyStatus.Approved;
        await fx.Db.SaveChangesAsync();
        var after = await service.GetAlertsAsync(candidate.UserId, 1, 20);
        Assert.True(after.Success, string.Join("; ", after.Errors));
        var restored = Assert.Single(after.Data!.Alerts);
        Assert.Equal(alertId, restored.Id);
        Assert.Equal(job.Id, restored.JobPostingId);
        Assert.Equal(1, after.Data.TotalCount);
        Assert.Equal(1, after.Data.UnreadCount);
        Assert.False(restored.IsRead);
        Assert.Equal(notificationCount, await fx.Db.Notifications.CountAsync());
        Assert.Equal(jobCount, await fx.Db.JobPostings.CountAsync());
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task Reapproval_AllowsSaveAndApply(CompanyStatus hiddenStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = AddCompany(fx, hiddenStatus);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Apply role",
            Description = "d",
            Status = JobStatus.Published,
            ExpiryDate = DateTime.UtcNow.AddDays(7)
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(
            fx, $"apply-{Guid.NewGuid():N}@example.com");

        var jobService = CreateJobService(fx);
        var applicationService = new ApplicationService(
            fx.Db, TimeProvider.System, NullLogger<ApplicationService>.Instance);

        var saveBefore = await jobService.SaveJobAsync(candidate.UserId, job.Id);
        var applyBefore = await applicationService.ApplyAsync(candidate.UserId, job.Id, resume.Id, null);
        Assert.False(saveBefore.Success);
        Assert.Equal(ServiceErrorType.NotFound, saveBefore.ErrorType);
        Assert.False(applyBefore.Success);
        Assert.Equal(ServiceErrorType.NotFound, applyBefore.ErrorType);
        Assert.Equal(0, await fx.Db.SavedJobs.CountAsync());
        Assert.Equal(0, await fx.Db.Applications.CountAsync());
        Assert.Equal(0, await fx.Db.ApplicationStatusHistories.CountAsync());

        var jobCount = await fx.Db.JobPostings.CountAsync();
        company.Status = CompanyStatus.Approved;
        await fx.Db.SaveChangesAsync();

        var saveAfter = await jobService.SaveJobAsync(candidate.UserId, job.Id);
        var applyAfter = await applicationService.ApplyAsync(candidate.UserId, job.Id, resume.Id, null);
        Assert.True(saveAfter.Success, string.Join("; ", saveAfter.Errors));
        Assert.True(applyAfter.Success, string.Join("; ", applyAfter.Errors));
        Assert.Equal(1, await fx.Db.SavedJobs.CountAsync());
        Assert.Equal(1, await fx.Db.Applications.CountAsync());
        Assert.Single(await fx.Db.ApplicationStatusHistories.ToListAsync());
        Assert.False(string.IsNullOrWhiteSpace(
            (await fx.Db.Applications.SingleAsync()).SnapshotJson));
        Assert.Equal(jobCount, await fx.Db.JobPostings.CountAsync());
    }

    [Theory]
    [InlineData(JobStatus.Draft, false)]
    [InlineData(JobStatus.Closed, false)]
    [InlineData(JobStatus.Expired, true)]
    [InlineData(JobStatus.Published, true)]
    [InlineData(JobStatus.Published, false)]
    public async Task CompanyActivity_HidesAndRestoresSavedJobsAndAlertFeed_WithoutFilteringJobState(
        JobStatus status, bool expired)
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [], status: status);
        var expiry = expired ? DateTime.UtcNow.AddDays(-1) : DateTime.UtcNow.AddDays(7);
        job.ExpiryDate = expiry;
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "activity-feed@example.com");
        var savedAt = DateTime.UtcNow.AddDays(-2);
        fx.Db.SavedJobs.Add(new SavedJob
        {
            CandidateId = candidate.Id, JobPostingId = job.Id, SavedAt = savedAt
        });
        var alert = new Notification
        {
            UserId = candidate.UserId, Kind = NotificationKinds.JobAlert,
            RelatedJobPostingId = job.Id, Title = "Existing alert", IsRead = false
        };
        var legacy = new Notification
        {
            UserId = candidate.UserId, Kind = NotificationKinds.JobAlert,
            RelatedJobPostingId = null, Title = "Legacy alert", IsRead = true
        };
        fx.Db.Notifications.AddRange(alert, legacy);
        await fx.Db.SaveChangesAsync();
        var jobs = CreateJobService(fx);
        var alerts = new JobAlertService(
            fx.Db, new StubMatchScoreService(), Options.Create(new JobAlertsOptions()),
            TimeProvider.System, NullLogger<JobAlertService>.Instance);

        var before = await jobs.GetSavedJobsAsync(candidate.UserId, 1, 20);
        Assert.Equal(status, Assert.Single(before.Data!.Items).Status);
        var feedBefore = await alerts.GetAlertsAsync(candidate.UserId, 1, 20);
        Assert.Equal(2, feedBefore.Data!.TotalCount);
        Assert.Equal(1, feedBefore.Data.UnreadCount);

        company.IsActive = false;
        await fx.Db.SaveChangesAsync();

        var hidden = await jobs.GetSavedJobsAsync(candidate.UserId, 1, 20);
        Assert.Empty(hidden.Data!.Items);
        Assert.Equal(0, hidden.Data.TotalCount);
        var feedHidden = await alerts.GetAlertsAsync(candidate.UserId, 1, 20);
        Assert.Equal(legacy.Id, Assert.Single(feedHidden.Data!.Alerts).Id);
        Assert.Equal(1, feedHidden.Data.TotalCount);
        Assert.Equal(0, feedHidden.Data.UnreadCount);

        company.IsActive = true;
        await fx.Db.SaveChangesAsync();

        var restored = await jobs.GetSavedJobsAsync(candidate.UserId, 1, 20);
        var item = Assert.Single(restored.Data!.Items);
        Assert.Equal(1, restored.Data.TotalCount);
        Assert.Equal(job.Id, item.JobId);
        Assert.Equal(status, item.Status);
        Assert.Equal(expiry, item.ExpiryDate);
        Assert.Equal(savedAt, item.SavedAt);
        var feedRestored = await alerts.GetAlertsAsync(candidate.UserId, 1, 20);
        Assert.Equal(2, feedRestored.Data!.TotalCount);
        Assert.Equal(1, feedRestored.Data.UnreadCount);
        Assert.Contains(feedRestored.Data.Alerts, row => row.Id == alert.Id && row.JobPostingId == job.Id);
        Assert.Equal(1, await fx.Db.SavedJobs.CountAsync());
        Assert.Equal(2, await fx.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task CompanyActivity_BlocksAlertGeneration_UntilReactivated()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, []);
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "activity-alert@example.com");
        company.IsActive = false;
        await fx.Db.SaveChangesAsync();
        var alerts = new JobAlertService(
            fx.Db, new StubMatchScoreService(),
            Options.Create(new JobAlertsOptions { MinMatchPercent = 60 }),
            TimeProvider.System, NullLogger<JobAlertService>.Instance);

        Assert.Equal(0, await alerts.NotifyForPublishedJobAsync(job.Id));
        Assert.Equal(0, await fx.Db.Notifications.CountAsync());

        company.IsActive = true;
        await fx.Db.SaveChangesAsync();

        Assert.Equal(1, await alerts.NotifyForPublishedJobAsync(job.Id));
        var alert = Assert.Single(await fx.Db.Notifications.ToListAsync());
        Assert.Equal(job.Id, alert.RelatedJobPostingId);
        Assert.Equal(candidate.UserId, alert.UserId);
        Assert.Equal(0, await alerts.NotifyForPublishedJobAsync(job.Id));
        Assert.Equal(1, await fx.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task CompanyActivity_BlocksSaveAndRecommendations_UntilReactivated()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(
            fx, "activity-match@example.com", ("C#", 3));
        var skill = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [(skill.Id, 10)]);
        company.IsActive = false;
        await fx.Db.SaveChangesAsync();
        var jobs = CreateJobService(fx);

        var save = await jobs.SaveJobAsync(candidate.UserId, job.Id);
        Assert.False(save.Success);
        Assert.Equal(ServiceErrorType.NotFound, save.ErrorType);
        Assert.Equal(0, await fx.Db.SavedJobs.CountAsync());
        var hidden = await jobs.GetJobsMatchingResumeAsync(candidate.UserId, resume.Id, null, null, 1, 1, 20);
        Assert.True(hidden.Success);
        Assert.Empty(hidden.Data!.Items);
        Assert.Equal(0, hidden.Data.TotalCount);

        company.IsActive = true;
        await fx.Db.SaveChangesAsync();

        Assert.True((await jobs.SaveJobAsync(candidate.UserId, job.Id)).Success);
        var restored = await jobs.GetJobsMatchingResumeAsync(candidate.UserId, resume.Id, null, null, 1, 1, 20);
        Assert.True(restored.Success);
        Assert.Equal(job.Id, Assert.Single(restored.Data!.Items).Id);
        Assert.Equal(1, restored.Data.TotalCount);
        Assert.Equal(1, await fx.Db.SavedJobs.CountAsync());
    }

    private sealed class StubMatchScoreService : IMatchScoreService
    {
        public Task<ServiceResult<MatchScoreDto>> GetAsync(
            string? userId,
            int jobId,
            int? resumeId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<MatchScoreDto>.Ok(
                new MatchScoreDto(true, jobId, resumeId ?? 0, 100, "test", null, null)));

        public Task<ServiceResult<IReadOnlyDictionary<int, double>>> GetStoredForJobsAsync(
            string? userId,
            IReadOnlyList<int> jobIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<IReadOnlyDictionary<int, double>>.Ok(
                new Dictionary<int, double>()));

        public Task<ServiceResult<AdvancedMatchScoreDto>> GetAdvancedAsync(
            string? userId,
            int jobId,
            int resumeId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ServiceResult<MatchTrainingResultDto>> TrainAsync(
            string? trainedByUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
