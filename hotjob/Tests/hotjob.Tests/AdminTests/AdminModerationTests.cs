using System.Security.Claims;
using hotjob.Controllers.Admin;
using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Models;
using hotjob.Models.Admin;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Company;
using hotjob.Repositories.Employer;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.AdminTests;

/// <summary>P1: admin moderation of companies, employers, job postings and reviews.</summary>
public sealed class AdminModerationTests
{
    private const string AdminEmail = "moderator@example.com";

    private static async Task<string> SignInAdminAsync(TestFixture fx)
    {
        var admin = await fx.SeedUserAsync(AdminEmail, role: AppRoles.Admin);
        fx.SignInAdminOnCircuit(admin.Id, AdminEmail); // HttpContextAccessor is AsyncLocal; would not survive this async helper
        return admin.Id;
    }

    private static async Task<List<AdminAuditLog>> AuditAsync(TestFixture fx) =>
        await fx.Db.AdminAuditLogs.AsNoTracking().OrderBy(log => log.Id).ToListAsync();

    // ------------------------------------------------------------------ companies

    [Fact]
    public async Task Companies_FilterByStatus_AndPendingQueue()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.Db.CompanyProfiles.AddRange(
            new CompanyProfile { CompanyName = "Alpha", Status = CompanyStatus.Pending },
            new CompanyProfile { CompanyName = "Bravo", Status = CompanyStatus.Approved },
            new CompanyProfile { CompanyName = "Charlie", Status = CompanyStatus.Rejected },
            new CompanyProfile { CompanyName = "Delta", Status = CompanyStatus.Pending });
        await fx.Db.SaveChangesAsync();
        var service = fx.CreateCompanyAdminService();

        var all = await service.ListAsync();
        var approved = await service.ListAsync(status: CompanyStatus.Approved);
        var pending = await service.ListPendingAsync();

        Assert.Equal(4, all.Data!.TotalCount);
        Assert.Equal(new[] { "Alpha", "Delta" }, all.Data.Items.Take(2).Select(c => c.CompanyName));
        Assert.Equal("Bravo", Assert.Single(approved.Data!.Items).CompanyName);
        Assert.Equal(new[] { "Alpha", "Delta" }, pending.Data!.Items.Select(c => c.CompanyName));
    }

    [Fact]
    public async Task Company_Approve_Reject_AndWrongStateIsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var company = new CompanyProfile { CompanyName = "Acme", Status = CompanyStatus.Pending };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var service = fx.CreateCompanyAdminService();

        Assert.True((await service.ApproveAsync(company.Id, adminId)).Success);
        var again = await service.ApproveAsync(company.Id, adminId);
        var noReason = await service.RejectAsync(company.Id, adminId, "  ");
        var rejected = await service.RejectAsync(company.Id, adminId, "  Fake licence  ");

        Assert.Equal(ServiceErrorType.Conflict, again.ErrorType);
        Assert.Equal(ServiceErrorType.Validation, noReason.ErrorType);
        Assert.True(rejected.Success);
        var stored = await fx.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == company.Id);
        Assert.Equal(CompanyStatus.Rejected, stored.Status);
        Assert.Equal("Fake licence", stored.RejectionReason);
        Assert.Equal(adminId, stored.VerifiedByAdminId);
        Assert.Equal(
            new[] { AuditActions.CompanyApprove, AuditActions.CompanyReject },
            (await AuditAsync(fx)).Select(log => log.Action));
    }

    [Fact]
    public async Task Company_DeactivateThenReactivate_ClearsDeactivationAndAuditsReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var company = new CompanyProfile { CompanyName = "Acme", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var service = fx.CreateCompanyAdminService();

        Assert.True((await service.DeactivateAsync(company.Id, adminId, "Scam reports")).Success);
        Assert.Equal(ServiceErrorType.Conflict, (await service.DeactivateAsync(company.Id, adminId)).ErrorType);
        Assert.True((await service.ReactivateAsync(company.Id, adminId)).Success);
        Assert.Equal(ServiceErrorType.Conflict, (await service.ReactivateAsync(company.Id, adminId)).ErrorType);

        var stored = await fx.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == company.Id);
        Assert.True(stored.IsActive);
        Assert.Null(stored.DeactivatedAt);
        Assert.Null(stored.DeactivatedByAdminId);
        var logs = await AuditAsync(fx);
        Assert.Equal(new[] { AuditActions.CompanyDeactivate, AuditActions.CompanyReactivate }, logs.Select(l => l.Action));
        Assert.Contains("Scam reports", logs[0].Detail);
    }

    // ------------------------------------------------------------------ employers

    [Fact]
    public async Task Employers_PendingQueue_ApproveAndReject_OnlyFromPending()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var pendingA = await MatchTestData.CreateEmployerAsync(fx, company.Id, "a@example.com");
        var pendingB = await MatchTestData.CreateEmployerAsync(fx, company.Id, "b@example.com");
        var active = await MatchTestData.CreateEmployerAsync(fx, company.Id, "c@example.com");
        pendingA.MembershipStatus = MembershipStatus.Pending;
        pendingB.MembershipStatus = MembershipStatus.Pending;
        await fx.Db.SaveChangesAsync();
        var service = fx.CreateEmployerAdminService();

        var queue = await service.ListPendingAsync();
        Assert.Equal(2, queue.Data!.TotalCount);
        Assert.All(queue.Data.Items, item => Assert.Equal("Pending", item.MembershipStatus));
        Assert.All(queue.Data.Items, item => Assert.Equal(company.CompanyName, item.CompanyName));

        Assert.True((await service.ApproveAsync(pendingA.Id, adminId)).Success);
        Assert.True((await service.RejectAsync(pendingB.Id, adminId, "Not on staff")).Success);
        Assert.Equal(ServiceErrorType.Conflict, (await service.ApproveAsync(active.Id, adminId)).ErrorType);
        Assert.Equal(ServiceErrorType.Conflict, (await service.RejectAsync(pendingA.Id, adminId, "late")).ErrorType);

        var employers = await fx.Db.Employers.AsNoTracking().ToDictionaryAsync(e => e.Id);
        Assert.Equal(MembershipStatus.Active, employers[pendingA.Id].MembershipStatus);
        Assert.Equal(MembershipStatus.Rejected, employers[pendingB.Id].MembershipStatus);
        Assert.Equal("Not on staff", employers[pendingB.Id].DeactivationReason);
        Assert.Equal(
            new[] { AuditActions.EmployerApprove, AuditActions.EmployerReject },
            (await AuditAsync(fx)).Select(log => log.Action));
    }

    // ------------------------------------------------------------------ jobs

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Published)]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Rejected)]
    public async Task Job_ApproveOrReject_OutsidePendingApproval_IsConflict(JobStatus status)
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [], status: status);
        var service = fx.CreateJobAdminService();

        Assert.Equal(ServiceErrorType.Conflict, (await service.ApproveAsync(job.Id, adminId)).ErrorType);
        Assert.Equal(ServiceErrorType.Conflict, (await service.RejectAsync(job.Id, adminId, "No")).ErrorType);
        Assert.Equal(status, (await fx.Db.JobPostings.AsNoTracking().SingleAsync(j => j.Id == job.Id)).Status);
        Assert.Empty(await AuditAsync(fx));
    }

    [Fact]
    public async Task Job_RejectPending_RecordsReasonReviewerAndAudit()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [], status: JobStatus.PendingApproval, title: "Spam role");
        var service = fx.CreateJobAdminService();

        var rejected = await service.RejectAsync(job.Id, adminId, "Misleading salary");
        var detail = await service.GetDetailAsync(job.Id);

        Assert.True(rejected.Success);
        Assert.Equal(nameof(JobStatus.Rejected), detail.Data!.Status);
        Assert.Equal("Misleading salary", detail.Data.RejectionReason);
        Assert.Equal(adminId, detail.Data.ReviewedByAdminId);
        Assert.NotNull(detail.Data.RejectedAt);
        Assert.Null(detail.Data.ApprovedAt);
        var log = Assert.Single(await AuditAsync(fx));
        Assert.Equal(AuditActions.JobReject, log.Action);
        Assert.Contains("Spam role", log.Detail);
    }

    [Fact]
    public async Task Jobs_FilterByStatus_PendingQueueOldestSubmissionFirst()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var newer = await MatchTestData.CreateJobAsync(fx, company.Id, [], status: JobStatus.PendingApproval, title: "Newer");
        var older = await MatchTestData.CreateJobAsync(fx, company.Id, [], status: JobStatus.PendingApproval, title: "Older");
        await MatchTestData.CreateJobAsync(fx, company.Id, [], status: JobStatus.Published, title: "Live");
        newer.SubmittedForApprovalAt = DateTime.UtcNow;
        older.SubmittedForApprovalAt = DateTime.UtcNow.AddDays(-2);
        await fx.Db.SaveChangesAsync();
        var service = fx.CreateJobAdminService();

        var pending = await service.ListPendingAsync();
        var published = await service.ListAsync(status: JobStatus.Published);

        Assert.Equal(new[] { "Older", "Newer" }, pending.Data!.Items.Select(j => j.Title));
        Assert.Equal("Live", Assert.Single(published.Data!.Items).Title);
    }

    // ------------------------------------------------------------------ reviews

    [Fact]
    public async Task HiddenReview_DisappearsFromPublicListingAndRating()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var (good, _) = await MatchTestData.CreateCandidateAsync(fx, "good@example.com");
        var (troll, _) = await MatchTestData.CreateCandidateAsync(fx, "troll@example.com");
        var kept = new Review { CompanyProfileId = company.Id, CandidateId = good.Id, RatingScore = 5, ReviewText = "Great" };
        var abusive = new Review { CompanyProfileId = company.Id, CandidateId = troll.Id, RatingScore = 1, ReviewText = "Abuse" };
        fx.Db.Reviews.AddRange(kept, abusive);
        await fx.Db.SaveChangesAsync();
        var publicService = new hotjob.Services.Company.CompanyService(
            new CompanyRepository(fx.Db), fx.Db, TimeProvider.System,
            NullLogger<hotjob.Services.Company.CompanyService>.Instance);
        var moderation = fx.CreateReviewAdminService();

        var hidden = await moderation.DeleteAsync(abusive.Id, adminId, "Harassment");
        var hiddenAgain = await moderation.DeleteAsync(abusive.Id, adminId);

        Assert.True(hidden.Success);
        Assert.Equal(ServiceErrorType.Conflict, hiddenAgain.ErrorType);
        var reviews = await publicService.GetReviewsAsync(company.Id, 1);
        Assert.Equal(kept.Id, Assert.Single(reviews.Data!.Items).Id);
        var detail = await publicService.GetDetailAsync(company.Id);
        Assert.Equal(1, detail.Data!.ReviewCount);
        Assert.Equal(5, detail.Data.AverageRating);
        Assert.Equal(1, (await moderation.ListAsync(isDeleted: true, companyId: company.Id)).Data!.TotalCount);
        var log = Assert.Single(await AuditAsync(fx));
        Assert.Equal(AuditActions.ReviewHide, log.Action);
        Assert.Contains("Harassment", log.Detail);
    }

    // ------------------------------------------------------------------ deactivation is enforced

    [Theory]
    [InlineData("employer")]
    [InlineData("company")]
    public async Task DeactivatedEmployerOrCompany_CannotPostJobsOrUseTheAts(string deactivated)
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var employer = await MatchTestData.CreateEmployerAsync(fx, company.Id, "hr@example.com");
        var skill = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var category = new JobCategory { Name = "Backend" };
        fx.Db.JobCategories.Add(category);
        await fx.Db.SaveChangesAsync();

        var result = deactivated == "employer"
            ? await fx.CreateEmployerAdminService().DeactivateAsync(employer.Id, adminId, "Left the company")
            : await fx.CreateCompanyAdminService().DeactivateAsync(company.Id, adminId, "Fraud");
        Assert.True(result.Success, string.Join("; ", result.Errors));
        fx.Db.ChangeTracker.Clear();

        var jobs = new JobService(
            fx.Db,
            new JobPostingRepository(fx.Db),
            new EntitlementService(fx.Db, TimeProvider.System),
            new StubJobAlertService(),
            NullLogger<JobService>.Instance, TimeProvider.System);
        var created = await jobs.CreateAsync(employer.UserId, new CreateJobRequest
        {
            Title = "Engineer",
            Description = "Build things.",
            ExperienceRequired = 1,
            JobType = JobType.FullTime,
            Status = JobStatus.Draft,
            Skills = [new() { SkillId = skill.Id, ImportanceWeight = 5 }],
            CategoryIds = [category.Id]
        });
        var applicants = await new ApplicantService(new ApplicantRepository(fx.Db), NullLogger<ApplicantService>.Instance)
            .ListAsync(employer.UserId, new hotjob.DTOs.Employer.ApplicantListQuery());

        Assert.Equal(ServiceErrorType.Unauthorized, created.ErrorType);
        Assert.Equal(ServiceErrorType.Unauthorized, applicants.ErrorType);
    }

    // ------------------------------------------------------------------ HTTP status mapping

    [Fact]
    public async Task Controller_MapsServiceFailuresToStatusCodes()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var draft = await MatchTestData.CreateJobAsync(fx, company.Id, [], status: JobStatus.Draft);
        var controller = new JobAdminController(fx.CreateJobAdminService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, adminId), new Claim(ClaimTypes.Role, AppRoles.Admin)],
                        "TestJwt"))
                }
            }
        };

        Assert.IsType<ConflictObjectResult>(await controller.Approve(draft.Id, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await controller.Approve(999_999, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Reject(draft.Id, new RejectJobRequest(" "), CancellationToken.None));
        var list = Assert.IsType<OkObjectResult>(await controller.List(status: JobStatus.Draft));
        Assert.Equal(1, Assert.IsType<hotjob.DTOs.Common.PagedResponse<JobListItemDto>>(list.Value).TotalCount);
    }
}
