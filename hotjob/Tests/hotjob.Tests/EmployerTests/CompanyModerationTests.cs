using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests;

/// <summary>
/// Employer-side company management: remove/restore recruiters, two-step job approval
/// (member → owner → admin) and company review viewing.
/// </summary>
public sealed class CompanyModerationTests
{
    private const string Password = "Str0ng!Pass";

    private sealed record Seeded(string OwnerUserId, int OwnerRowId, string MemberUserId, int MemberRowId, int CompanyId);

    private static async Task<Seeded> SeedAsync(TestFixture fx, string prefix = "co")
    {
        var owner = await fx.SeedUserAsync($"{prefix}-owner@example.com", Password, AppRoles.Employer);
        var member = await fx.SeedUserAsync($"{prefix}-member@example.com", Password, AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = $"Company {prefix}", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var ownerRow = new Employer
        {
            UserId = owner.Id, CompanyProfileId = company.Id, FullName = "Owner",
            IsOwner = true, MembershipStatus = MembershipStatus.Active
        };
        var memberRow = new Employer
        {
            UserId = member.Id, CompanyProfileId = company.Id, FullName = "Recruiter",
            MembershipStatus = MembershipStatus.Active
        };
        fx.Db.Employers.AddRange(ownerRow, memberRow);
        await fx.Db.SaveChangesAsync();
        return new Seeded(owner.Id, ownerRow.Id, member.Id, memberRow.Id, company.Id);
    }

    private static EmployerCompanyService CompanyService(TestFixture fx) =>
        new(fx.Db, NullLogger<EmployerCompanyService>.Instance);

    private static JobService JobService(TestFixture fx) =>
        new(fx.Db, new JobPostingRepository(fx.Db), NullLogger<JobService>.Instance);

    private static ReviewService ReviewService(TestFixture fx) =>
        new(fx.Db, NullLogger<ReviewService>.Instance);

    private static CreateJobRequest NewJob(JobStatus status, string title = "Backend .NET") => new()
    {
        Title = title,
        Description = "Build APIs.",
        JobType = JobType.FullTime,
        Status = status
    };

    // ---------- remove / restore recruiter ----------

    [Fact]
    public async Task RemoveMember_Active_SoftRemovesAndBlocksAccess()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);

        var result = await CompanyService(fx).RemoveMemberAsync(s.OwnerUserId, s.MemberRowId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(MembershipStatus.Removed, result.Data!.MembershipStatus);
        var row = await fx.Db.Employers.AsNoTracking().SingleAsync(e => e.Id == s.MemberRowId);
        Assert.Equal(MembershipStatus.Removed, row.MembershipStatus);
        Assert.NotNull(row.RemovedAt);
        Assert.Equal(s.OwnerRowId, row.RemovedByEmployerId);

        // The removed recruiter loses every employer feature immediately.
        var jobs = await JobService(fx).ListAsync(s.MemberUserId, null, null, 1, 20);
        Assert.Equal(ServiceErrorType.Unauthorized, jobs.ErrorType);
        var reviews = await ReviewService(fx).GetCompanyReviewsAsync(s.MemberUserId, 1, 10);
        Assert.Equal(ServiceErrorType.Unauthorized, reviews.ErrorType);
    }

    [Fact]
    public async Task RemoveMember_KeepsTheirJobsWithTheCompany()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var created = await JobService(fx).CreateAsync(s.MemberUserId, NewJob(JobStatus.Draft));
        Assert.True(created.Success, string.Join("; ", created.Errors));

        await CompanyService(fx).RemoveMemberAsync(s.OwnerUserId, s.MemberRowId);

        var job = await fx.Db.JobPostings.AsNoTracking().SingleAsync(j => j.Id == created.Data!.Id);
        Assert.Equal(s.CompanyId, job.CompanyProfileId);
        Assert.Equal(s.MemberRowId, job.CreatedByEmployerId);
    }

    [Fact]
    public async Task RemoveMember_Owner_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);

        var result = await CompanyService(fx).RemoveMemberAsync(s.OwnerUserId, s.OwnerRowId);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task RemoveMember_ByNonOwner_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);

        var result = await CompanyService(fx).RemoveMemberAsync(s.MemberUserId, s.OwnerRowId);

        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task RemoveMember_OtherCompany_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var mine = await SeedAsync(fx, "a");
        var other = await SeedAsync(fx, "b");

        var result = await CompanyService(fx).RemoveMemberAsync(mine.OwnerUserId, other.MemberRowId);

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task RemoveMember_Pending_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        (await fx.Db.Employers.SingleAsync(e => e.Id == s.MemberRowId)).MembershipStatus = MembershipStatus.Pending;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var result = await CompanyService(fx).RemoveMemberAsync(s.OwnerUserId, s.MemberRowId);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task RestoreMember_Removed_ReactivatesAndClearsAudit()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var svc = CompanyService(fx);
        await svc.RemoveMemberAsync(s.OwnerUserId, s.MemberRowId);
        fx.Db.ChangeTracker.Clear();

        var result = await svc.RestoreMemberAsync(s.OwnerUserId, s.MemberRowId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var row = await fx.Db.Employers.AsNoTracking().SingleAsync(e => e.Id == s.MemberRowId);
        Assert.Equal(MembershipStatus.Active, row.MembershipStatus);
        Assert.Null(row.RemovedAt);
        Assert.Null(row.RemovedByEmployerId);
    }

    [Fact]
    public async Task RestoreMember_Active_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);

        var result = await CompanyService(fx).RestoreMemberAsync(s.OwnerUserId, s.MemberRowId);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task GetPage_RemovedMembers_VisibleToOwnerOnly()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var third = await fx.SeedUserAsync("third@example.com", Password, AppRoles.Employer);
        fx.Db.Employers.Add(new Employer
        {
            UserId = third.Id, CompanyProfileId = s.CompanyId, FullName = "Gone",
            MembershipStatus = MembershipStatus.Removed, RemovedAt = DateTime.UtcNow
        });
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var asOwner = await CompanyService(fx).GetPageAsync(s.OwnerUserId);
        var asMember = await CompanyService(fx).GetPageAsync(s.MemberUserId);

        Assert.Contains(asOwner.Data!.Members, m => m.MembershipStatus == MembershipStatus.Removed);
        Assert.DoesNotContain(asMember.Data!.Members, m => m.MembershipStatus == MembershipStatus.Removed);
    }

    // ---------- two-step job approval ----------

    [Fact]
    public async Task CreateJob_MemberSubmits_WaitsForOwner()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);

        var created = await JobService(fx).CreateAsync(s.MemberUserId, NewJob(JobStatus.Published));

        Assert.True(created.Success, string.Join("; ", created.Errors));
        Assert.Equal(JobStatus.PendingCompanyApproval, created.Data!.Status);
        Assert.NotNull(created.Data.SubmittedForApprovalAt);
    }

    [Fact]
    public async Task CreateJob_OwnerSubmits_GoesStraightToAdmin()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);

        var created = await JobService(fx).CreateAsync(s.OwnerUserId, NewJob(JobStatus.Published));

        Assert.True(created.Success, string.Join("; ", created.Errors));
        Assert.Equal(JobStatus.PendingApproval, created.Data!.Status);
    }

    [Fact]
    public async Task OwnerApprovesMemberJob_ForwardsToAdminQueue()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var created = await JobService(fx).CreateAsync(s.MemberUserId, NewJob(JobStatus.Published));
        var svc = CompanyService(fx);

        var pending = await svc.ListPendingJobsAsync(s.OwnerUserId, 1, 20);
        Assert.True(pending.Success, string.Join("; ", pending.Errors));
        var item = Assert.Single(pending.Data!.Items);
        Assert.Equal("Recruiter", item.CreatedByName);

        var approved = await svc.ApproveJobAsync(s.OwnerUserId, created.Data!.Id);

        Assert.True(approved.Success, string.Join("; ", approved.Errors));
        Assert.Equal(JobStatus.PendingApproval, approved.Data!.Status);
        var job = await fx.Db.JobPostings.AsNoTracking().SingleAsync(j => j.Id == created.Data.Id);
        Assert.Equal(JobStatus.PendingApproval, job.Status);
        Assert.Equal(s.OwnerRowId, job.CompanyReviewedByEmployerId);
        Assert.NotNull(job.CompanyReviewedAt);
        Assert.Empty((await svc.ListPendingJobsAsync(s.OwnerUserId, 1, 20)).Data!.Items);
    }

    [Fact]
    public async Task OwnerRejectsMemberJob_StoresReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var created = await JobService(fx).CreateAsync(s.MemberUserId, NewJob(JobStatus.Published));

        var rejected = await CompanyService(fx).RejectJobAsync(
            s.OwnerUserId, created.Data!.Id, new RejectCompanyJobRequest { Reason = "  Salary range missing  " });

        Assert.True(rejected.Success, string.Join("; ", rejected.Errors));
        var job = await fx.Db.JobPostings.AsNoTracking().SingleAsync(j => j.Id == created.Data.Id);
        Assert.Equal(JobStatus.Rejected, job.Status);
        Assert.Equal("Salary range missing", job.RejectionReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OwnerRejectsMemberJob_BlankReason_ReturnsValidation(string reason)
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var created = await JobService(fx).CreateAsync(s.MemberUserId, NewJob(JobStatus.Published));

        var rejected = await CompanyService(fx).RejectJobAsync(
            s.OwnerUserId, created.Data!.Id, new RejectCompanyJobRequest { Reason = reason });

        Assert.Equal(ServiceErrorType.Validation, rejected.ErrorType);
    }

    [Fact]
    public async Task OwnerApprove_JobNotWaitingForCompany_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var draft = await JobService(fx).CreateAsync(s.MemberUserId, NewJob(JobStatus.Draft));

        var result = await CompanyService(fx).ApproveJobAsync(s.OwnerUserId, draft.Data!.Id);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task MemberCannotApproveJobs()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var created = await JobService(fx).CreateAsync(s.MemberUserId, NewJob(JobStatus.Published));

        var result = await CompanyService(fx).ApproveJobAsync(s.MemberUserId, created.Data!.Id);

        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task OwnerApprove_OtherCompanyJob_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var mine = await SeedAsync(fx, "a");
        var other = await SeedAsync(fx, "b");
        var foreign = await JobService(fx).CreateAsync(other.MemberUserId, NewJob(JobStatus.Published));

        var result = await CompanyService(fx).ApproveJobAsync(mine.OwnerUserId, foreign.Data!.Id);

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task UpdateJob_DraftSubmittedByMember_GoesToOwnerQueue()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var jobs = JobService(fx);
        var draft = await jobs.CreateAsync(s.MemberUserId, NewJob(JobStatus.Draft));

        var updated = await jobs.UpdateAsync(s.MemberUserId, draft.Data!.Id, new UpdateJobRequest
        {
            Title = "Backend .NET", Description = "Build APIs.", JobType = JobType.FullTime, Status = JobStatus.Published
        });

        Assert.True(updated.Success, string.Join("; ", updated.Errors));
        Assert.Equal(JobStatus.PendingCompanyApproval, updated.Data!.Status);
    }

    [Fact]
    public async Task UpdateJob_RejectedThenResubmitted_ClearsReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var jobs = JobService(fx);
        var created = await jobs.CreateAsync(s.MemberUserId, NewJob(JobStatus.Published));
        await CompanyService(fx).RejectJobAsync(s.OwnerUserId, created.Data!.Id, new RejectCompanyJobRequest { Reason = "Fix title" });
        fx.Db.ChangeTracker.Clear();

        var updated = await jobs.UpdateAsync(s.MemberUserId, created.Data.Id, new UpdateJobRequest
        {
            Title = "Senior Backend .NET", Description = "Build APIs.", JobType = JobType.FullTime, Status = JobStatus.Published
        });

        Assert.True(updated.Success, string.Join("; ", updated.Errors));
        Assert.Equal(JobStatus.PendingCompanyApproval, updated.Data!.Status);
        Assert.Null(updated.Data.RejectionReason);
    }

    [Fact]
    public async Task UpdateJob_PendingAdminJob_CannotBePublishedDirectly()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var jobs = JobService(fx);
        var created = await jobs.CreateAsync(s.OwnerUserId, NewJob(JobStatus.Published));

        // Re-saving with "Published" keeps it in the admin queue instead of going live.
        var updated = await jobs.UpdateAsync(s.OwnerUserId, created.Data!.Id, new UpdateJobRequest
        {
            Title = "Backend .NET", Description = "Build APIs.", JobType = JobType.FullTime, Status = JobStatus.Published
        });

        Assert.True(updated.Success, string.Join("; ", updated.Errors));
        Assert.Equal(JobStatus.PendingApproval, updated.Data!.Status);
    }

    [Theory]
    [InlineData(JobStatus.PendingApproval)]
    [InlineData(JobStatus.Approved)]
    public async Task UpdateJob_MemberEditsAfterOwnerApproval_GoesBackToOwner(JobStatus ownerApprovedState)
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var jobs = JobService(fx);
        var created = await jobs.CreateAsync(s.MemberUserId, NewJob(JobStatus.Published));
        await CompanyService(fx).ApproveJobAsync(s.OwnerUserId, created.Data!.Id);
        (await fx.Db.JobPostings.SingleAsync(j => j.Id == created.Data.Id)).Status = ownerApprovedState;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var updated = await jobs.UpdateAsync(s.MemberUserId, created.Data.Id, new UpdateJobRequest
        {
            Title = "Changed after approval", Description = "Different content.", JobType = JobType.FullTime, Status = JobStatus.Published
        });

        Assert.True(updated.Success, string.Join("; ", updated.Errors));
        Assert.Equal(JobStatus.PendingCompanyApproval, updated.Data!.Status);
        var job = await fx.Db.JobPostings.AsNoTracking().SingleAsync(j => j.Id == created.Data.Id);
        Assert.Null(job.CompanyReviewedAt);
    }

    [Fact]
    public async Task UpdateJob_PublishedJob_CannotMoveBackToDraft()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        var created = await JobService(fx).CreateAsync(s.OwnerUserId, NewJob(JobStatus.Draft));
        (await fx.Db.JobPostings.SingleAsync(j => j.Id == created.Data!.Id)).Status = JobStatus.Published;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var updated = await JobService(fx).UpdateAsync(s.OwnerUserId, created.Data!.Id, new UpdateJobRequest
        {
            Title = "Backend .NET", Description = "Build APIs.", JobType = JobType.FullTime, Status = JobStatus.Draft
        });

        Assert.Equal(ServiceErrorType.Validation, updated.ErrorType);
    }

    [Fact]
    public async Task DeactivatedEmployer_CannotCreateJobs()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        (await fx.Db.Employers.SingleAsync(e => e.Id == s.MemberRowId)).IsActive = false;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var created = await JobService(fx).CreateAsync(s.MemberUserId, NewJob(JobStatus.Draft));

        Assert.Equal(ServiceErrorType.Unauthorized, created.ErrorType);
    }

    // ---------- company reviews (employer view) ----------

    private static async Task SeedReviewsAsync(TestFixture fx, int companyId, params (int Rating, bool Deleted)[] reviews)
    {
        var i = 0;
        foreach (var (rating, deleted) in reviews)
        {
            var user = await fx.SeedUserAsync($"cand{companyId}-{i++}@example.com", Password, AppRoles.Candidate);
            var candidate = new Candidate { UserId = user.Id, FullName = $"Candidate {i}" };
            fx.Db.Candidates.Add(candidate);
            await fx.Db.SaveChangesAsync();
            fx.Db.Reviews.Add(new Review
            {
                CompanyProfileId = companyId,
                CandidateId = candidate.Id,
                RatingScore = rating,
                ReviewText = $"Review {i}",
                IsDeleted = deleted
            });
        }

        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reviews_ExcludeModeratedAndFilterByRating()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        await SeedReviewsAsync(fx, s.CompanyId, (5, false), (5, false), (2, false), (1, true));

        var all = await ReviewService(fx).GetCompanyReviewsAsync(s.MemberUserId, 1, 10);
        var fiveStars = await ReviewService(fx).GetCompanyReviewsAsync(s.MemberUserId, 1, 10, rating: 5);
        var stats = await ReviewService(fx).GetCompanyRatingStatsAsync(s.MemberUserId);

        Assert.Equal(3, all.Data!.TotalCount);
        Assert.Equal(2, fiveStars.Data!.TotalCount);
        Assert.All(fiveStars.Data.Items, r => Assert.Equal(5, r.RatingScore));
        Assert.Equal(3, stats.Data!.TotalReviews);
        Assert.Equal(0, stats.Data.OneStarCount);
        Assert.Equal(4.0, stats.Data.AverageRating);
    }

    [Fact]
    public async Task Reviews_InvalidRatingFilter_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);

        var result = await ReviewService(fx).GetCompanyReviewsAsync(s.OwnerUserId, 1, 10, rating: 6);

        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Reviews_OnlyOwnCompany()
    {
        await using var fx = await TestFixture.CreateAsync();
        var mine = await SeedAsync(fx, "a");
        var other = await SeedAsync(fx, "b");
        await SeedReviewsAsync(fx, other.CompanyId, (1, false));

        var result = await ReviewService(fx).GetCompanyReviewsAsync(mine.OwnerUserId, 1, 10);

        Assert.Equal(0, result.Data!.TotalCount);
    }

    [Fact]
    public async Task Reviews_DeactivatedCompany_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var s = await SeedAsync(fx);
        (await fx.Db.CompanyProfiles.SingleAsync(c => c.Id == s.CompanyId)).IsActive = false;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var result = await ReviewService(fx).GetCompanyReviewsAsync(s.OwnerUserId, 1, 10);

        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }
}
