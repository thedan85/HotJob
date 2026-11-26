using hotjob.Data;
using hotjob.DTOs.Job;
using hotjob.Models;
using hotjob.Models.Admin;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.AdminTests;

/// <summary>
/// Admin phase 0: company review results reach the employers' inbox, and an admin removal closes a
/// posting for good while keeping its applications and history.
/// </summary>
public sealed class AdminNotificationAndRemovalTests
{
    private const string AdminEmail = "moderator@example.com";

    private static async Task<string> SignInAdminAsync(TestFixture fx)
    {
        var admin = await fx.SeedUserAsync(AdminEmail, role: AppRoles.Admin);
        fx.SignInAdminOnCircuit(admin.Id, AdminEmail);
        return admin.Id;
    }

    private static JobService CreateJobService(TestFixture fx) =>
        new(
            fx.Db,
            new JobPostingRepository(fx.Db),
            new EntitlementService(fx.Db, TimeProvider.System),
            new StubJobAlertService(),
            NullLogger<JobService>.Instance);

    // ------------------------------------------------------------------ company review notifications

    private static async Task<(CompanyProfile Company, Employer Owner, Employer Pending, Employer Deactivated)> SeedPendingCompanyAsync(TestFixture fx)
    {
        var company = await MatchTestData.CreateCompanyAsync(fx);
        company.Status = CompanyStatus.Pending;
        await fx.Db.SaveChangesAsync();
        var owner = await MatchTestData.CreateEmployerAsync(fx, company.Id, "owner@example.com");
        var pending = await MatchTestData.CreateEmployerAsync(fx, company.Id, "pending@example.com");
        pending.IsOwner = false;
        pending.MembershipStatus = MembershipStatus.Pending;
        var deactivated = await MatchTestData.CreateEmployerAsync(fx, company.Id, "gone@example.com");
        deactivated.IsOwner = false;
        deactivated.IsActive = false;
        await fx.Db.SaveChangesAsync();
        return (company, owner, pending, deactivated);
    }

    [Fact]
    public async Task ApproveCompany_NotifiesActiveMembersOnly()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var (company, owner, _, _) = await SeedPendingCompanyAsync(fx);

        var result = await fx.CreateCompanyAdminService().ApproveAsync(company.Id, adminId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var notification = Assert.Single(await fx.Db.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(owner.UserId, notification.UserId);
        Assert.Equal(NotificationKinds.CompanyReview, notification.Kind);
        Assert.Equal("Company approved", notification.Title);
        Assert.Contains(company.CompanyName, notification.Content);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task RejectCompany_SendsReason_ToEmployerInbox()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var (company, owner, _, _) = await SeedPendingCompanyAsync(fx);

        var result = await fx.CreateCompanyAdminService().RejectAsync(company.Id, adminId, "  Tax code does not match.  ");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        // The employer-only account reads it through the shared notification feed.
        var feed = await new CandidateService(fx.Db).GetMyNotificationsAsync(owner.UserId, 1, 20);
        Assert.True(feed.Success, string.Join("; ", feed.Errors));
        var item = Assert.Single(feed.Data!.Items);
        Assert.Equal("Company rejected", item.Title);
        Assert.Contains("Reason: Tax code does not match.", item.Content);
    }

    [Fact]
    public async Task FailedReview_SendsNothing()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var (company, _, _, _) = await SeedPendingCompanyAsync(fx);
        var service = fx.CreateCompanyAdminService();

        Assert.Equal(ServiceErrorType.Validation, (await service.RejectAsync(company.Id, adminId, " ")).ErrorType);
        Assert.True((await service.ApproveAsync(company.Id, adminId)).Success);
        Assert.Equal(ServiceErrorType.Conflict, (await service.ApproveAsync(company.Id, adminId)).ErrorType);

        Assert.Equal(1, await fx.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task DeactivateAndReactivateCompany_NotifyActiveMembers()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var (company, owner, _, _) = await SeedPendingCompanyAsync(fx);
        company.Status = CompanyStatus.Approved;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();
        var service = fx.CreateCompanyAdminService();

        Assert.True((await service.DeactivateAsync(company.Id, adminId, "Fake listings.")).Success);
        Assert.True((await service.ReactivateAsync(company.Id, adminId)).Success);
        Assert.Equal(ServiceErrorType.Conflict, (await service.ReactivateAsync(company.Id, adminId)).ErrorType);

        var notifications = await fx.Db.Notifications.AsNoTracking().OrderBy(n => n.Id).ToListAsync();
        Assert.Equal(["Company suspended", "Company reactivated"], notifications.Select(n => n.Title).ToArray());
        Assert.All(notifications, n =>
        {
            Assert.Equal(owner.UserId, n.UserId);
            Assert.Equal(NotificationKinds.CompanyReview, n.Kind);
        });
        Assert.Contains("Reason: Fake listings.", notifications[0].Content);
    }

    // ------------------------------------------------------------------ job removal

    private sealed record Seed(
        string AdminId,
        Employer Owner,
        JobPosting Job,
        Application Pending,
        Application Interviewing,
        Application Offered,
        int SkillId);

    private static async Task<Seed> SeedJobWithApplicationsAsync(TestFixture fx)
    {
        var adminId = await SignInAdminAsync(fx);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var owner = await MatchTestData.CreateEmployerAsync(fx, company.Id, "owner@example.com");
        var skill = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [(skill.Id, 5)], title: "Backend dev");
        var (c1, r1) = await MatchTestData.CreateCandidateAsync(fx, "c1@example.com", ("C#", 2));
        var (c2, r2) = await MatchTestData.CreateCandidateAsync(fx, "c2@example.com", ("C#", 3));
        var (c3, r3) = await MatchTestData.CreateCandidateAsync(fx, "c3@example.com", ("C#", 4));
        var pending = await MatchTestData.CreateApplicationAsync(fx, job, r1, c1, ApplicationStatus.Pending);
        var interviewing = await MatchTestData.CreateApplicationAsync(fx, job, r2, c2, ApplicationStatus.Interviewing);
        var offered = await MatchTestData.CreateApplicationAsync(fx, job, r3, c3, ApplicationStatus.Offered);
        fx.Db.ChangeTracker.Clear();
        return new Seed(adminId, owner, job, pending, interviewing, offered, skill.Id);
    }

    [Fact]
    public async Task RemoveJob_ClosesJob_KeepsApplications_AndNotifies()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = await SeedJobWithApplicationsAsync(fx);

        var result = await fx.CreateJobAdminService().RemoveAsync(seed.Job.Id, seed.AdminId, "  Misleading salary.  ");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Data);

        fx.Db.ChangeTracker.Clear();
        var job = await fx.Db.JobPostings.Include(j => j.Applications).SingleAsync(j => j.Id == seed.Job.Id);
        Assert.Equal(JobStatus.Closed, job.Status);
        Assert.NotNull(job.ModeratedAt);
        Assert.Equal(seed.AdminId, job.ModeratedByAdminId);
        Assert.Equal("Misleading salary.", job.ModerationReason);
        Assert.Equal(3, job.Applications.Count);
        Assert.Equal(ApplicationStatus.Rejected, job.Applications.Single(a => a.Id == seed.Pending.Id).CurrentStatus);
        Assert.Equal(ApplicationStatus.Rejected, job.Applications.Single(a => a.Id == seed.Interviewing.Id).CurrentStatus);
        Assert.Equal(ApplicationStatus.Offered, job.Applications.Single(a => a.Id == seed.Offered.Id).CurrentStatus);

        var history = await fx.Db.ApplicationStatusHistories.AsNoTracking().ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, row =>
        {
            Assert.Equal(ApplicationHistoryNotes.JobRemovedByAdmin, row.Notes);
            Assert.Equal(seed.AdminId, row.ChangedByUserId);
        });

        var notifications = await fx.Db.Notifications.AsNoTracking().ToListAsync();
        Assert.Equal(2, notifications.Count(n => n.Title == "Application closed"));
        var employerNote = Assert.Single(notifications, n => n.UserId == seed.Owner.UserId);
        Assert.Equal(NotificationKinds.JobModeration, employerNote.Kind);
        Assert.Equal(seed.Job.Id, employerNote.RelatedJobPostingId);
        Assert.Contains("Misleading salary.", employerNote.Content);

        var log = await fx.Db.AdminAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(AuditActions.JobRemove, log.Action);
        Assert.Equal(AuditTargetTypes.Job, log.TargetType);
        Assert.Equal(seed.Job.Id.ToString(), log.TargetId);
        Assert.Contains("Misleading salary.", log.Detail);
    }

    [Fact]
    public async Task RemoveJob_Validation_NotFound_AndTwiceIsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = await SeedJobWithApplicationsAsync(fx);
        var service = fx.CreateJobAdminService();

        Assert.Equal(ServiceErrorType.Validation, (await service.RemoveAsync(seed.Job.Id, seed.AdminId, " ")).ErrorType);
        Assert.Equal(ServiceErrorType.Validation, (await service.RemoveAsync(seed.Job.Id, seed.AdminId, new string('x', 501))).ErrorType);
        Assert.Equal(ServiceErrorType.NotFound, (await service.RemoveAsync(9999, seed.AdminId, "Spam.")).ErrorType);
        Assert.Empty(await fx.Db.AdminAuditLogs.ToListAsync());

        Assert.True((await service.RemoveAsync(seed.Job.Id, seed.AdminId, "Spam.")).Success);
        fx.Db.ChangeTracker.Clear();
        var again = await service.RemoveAsync(seed.Job.Id, seed.AdminId, "Spam.");

        Assert.Equal(ServiceErrorType.Conflict, again.ErrorType);
        Assert.Equal(1, await fx.Db.AdminAuditLogs.CountAsync());
    }

    [Fact]
    public async Task RemovedJob_ShowsReasonToEmployer_AndCannotBeReopened()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = await SeedJobWithApplicationsAsync(fx);
        Assert.True((await fx.CreateJobAdminService().RemoveAsync(seed.Job.Id, seed.AdminId, "Spam.")).Success);
        fx.Db.ChangeTracker.Clear();
        var jobs = CreateJobService(fx);

        var detail = await jobs.GetAsync(seed.Owner.UserId, seed.Job.Id);
        Assert.True(detail.Success, string.Join("; ", detail.Errors));
        Assert.Equal("Spam.", detail.Data!.ModerationReason);

        var list = await new JobPostingRepository(fx.Db).ListOwnedAsync(seed.Job.CompanyProfileId, null, null, 1, 20);
        Assert.Equal("Spam.", Assert.Single(list.Items).ModerationReason);

        var reopen = await jobs.UpdateAsync(seed.Owner.UserId, seed.Job.Id, new UpdateJobRequest
        {
            Title = "Backend dev",
            Description = "Test job description.",
            ExperienceRequired = 1,
            JobType = JobType.FullTime,
            Status = JobStatus.Published,
            Skills = [new JobSkillInput { SkillId = seed.SkillId, ImportanceWeight = 5 }]
        });

        Assert.Equal(ServiceErrorType.Conflict, reopen.ErrorType);
        Assert.Equal(JobStatus.Closed, (await fx.Db.JobPostings.AsNoTracking().SingleAsync(j => j.Id == seed.Job.Id)).Status);
    }

    [Fact]
    public async Task AdminDelete_JobWithApplications_IsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = await SeedJobWithApplicationsAsync(fx);

        var result = await fx.CreateAdminService().DeleteJobAsync(seed.Job.Id);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.True(await fx.Db.JobPostings.AnyAsync(j => j.Id == seed.Job.Id));
        Assert.Equal(3, await fx.Db.Applications.CountAsync());
        Assert.Empty(await fx.Db.AdminAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task EmployerClose_NotifiesOpenCandidates()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = await SeedJobWithApplicationsAsync(fx);

        var result = await CreateJobService(fx).DeleteOrCloseAsync(seed.Owner.UserId, seed.Job.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.Closed);
        var notifications = await fx.Db.Notifications.AsNoTracking().ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n =>
        {
            Assert.Equal("Application closed", n.Title);
            Assert.Equal(seed.Job.Id, n.RelatedJobPostingId);
        });
        Assert.All(
            await fx.Db.ApplicationStatusHistories.AsNoTracking().ToListAsync(),
            row => Assert.Equal(ApplicationHistoryNotes.JobClosed, row.Notes));
    }

    [Theory]
    [InlineData(ApplicationHistoryNotes.JobClosed, true)]
    [InlineData(ApplicationHistoryNotes.JobRemovedByAdmin, true)]
    [InlineData("Not a fit", false)]
    [InlineData(null, false)]
    public void IsAutoRejection_MatchesSystemNotesOnly(string? note, bool expected) =>
        Assert.Equal(expected, ApplicationHistoryNotes.IsAutoRejection(note));
}
