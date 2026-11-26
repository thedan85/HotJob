using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Admin;
using hotjob.Models.Application;
using hotjob.Models.Billing;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.AdminTests;

/// <summary>Admin phase 1 (A1.4): the admin company page overview.</summary>
public sealed class AdminCompanyOverviewTests
{
    [Fact]
    public async Task Overview_ReturnsMembersJobsReviewsPlanAndHistory()
    {
        await using var fx = await TestFixture.CreateAsync();
        var admin = await fx.SeedUserAsync("moderator@example.com", role: AppRoles.Admin);
        fx.SignInAdminOnCircuit(admin.Id, "moderator@example.com");

        var company = await MatchTestData.CreateCompanyAsync(fx);
        company.Status = CompanyStatus.Pending;
        await fx.Db.SaveChangesAsync();
        var owner = await MatchTestData.CreateEmployerAsync(fx, company.Id, "owner@acme.test");
        var member = await MatchTestData.CreateEmployerAsync(fx, company.Id, "member@acme.test");
        member.IsOwner = false;
        member.MembershipStatus = MembershipStatus.Pending;
        await fx.Db.SaveChangesAsync();

        var published = await MatchTestData.CreateJobAsync(fx, company.Id, [], title: "Published role");
        await MatchTestData.CreateJobAsync(fx, company.Id, [], status: JobStatus.Draft, title: "Draft role");
        await MatchTestData.CreateJobAsync(fx, company.Id, [], status: JobStatus.Draft, title: "Another draft");
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "c1@example.com");
        await MatchTestData.CreateApplicationAsync(fx, published, resume, candidate, ApplicationStatus.Pending);

        var (reviewer2, _) = await MatchTestData.CreateCandidateAsync(fx, "c2@example.com");
        var (reviewer3, _) = await MatchTestData.CreateCandidateAsync(fx, "c3@example.com");
        fx.Db.Reviews.AddRange(
            new Review { CompanyProfileId = company.Id, CandidateId = candidate.Id, RatingScore = 5 },
            new Review { CompanyProfileId = company.Id, CandidateId = reviewer2.Id, RatingScore = 2 },
            new Review { CompanyProfileId = company.Id, CandidateId = reviewer3.Id, RatingScore = 1, IsDeleted = true });
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var service = fx.CreateCompanyAdminService();
        Assert.True((await service.ApproveAsync(company.Id, admin.Id)).Success);
        fx.Db.ChangeTracker.Clear();

        var result = await service.GetOverviewAsync(company.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var overview = result.Data!;
        Assert.Equal(nameof(CompanyStatus.Approved), overview.Profile.Status);

        Assert.Equal(2, overview.Members.Count);
        Assert.Equal(owner.Id, overview.Members[0].EmployerId);
        Assert.True(overview.Members[0].IsOwner);
        Assert.Equal("owner@acme.test", overview.Members[0].Email);
        Assert.Equal(nameof(MembershipStatus.Pending), overview.Members[1].MembershipStatus);

        Assert.Equal(
            [(nameof(JobStatus.Draft), 2), (nameof(JobStatus.Published), 1)],
            overview.JobCounts.Select(c => (c.Status, c.Count)).OrderBy(c => c.Status).ToArray());
        Assert.Equal(3, overview.RecentJobs.Count);
        Assert.Equal(1, overview.RecentJobs.Single(j => j.Id == published.Id).ApplicationCount);

        Assert.Equal(2, overview.ReviewCount);
        Assert.Equal(3.5, overview.AverageRating);

        Assert.NotNull(overview.Plan);
        Assert.Equal(Plan.FreeCode, overview.Plan!.PlanCode);
        Assert.Equal(3, overview.Plan.ActiveJobCount); // drafts hold a quota slot too

        Assert.Equal(AuditActions.CompanyApprove, Assert.Single(overview.History).Action);
    }

    [Fact]
    public async Task Overview_EmptyCompany_HasNoRatingOrJobs()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);

        var overview = (await fx.CreateCompanyAdminService().GetOverviewAsync(company.Id)).Data!;

        Assert.Empty(overview.Members);
        Assert.Empty(overview.JobCounts);
        Assert.Empty(overview.RecentJobs);
        Assert.Equal(0, overview.ReviewCount);
        Assert.Null(overview.AverageRating);
        Assert.Empty(overview.History);
    }

    [Fact]
    public async Task Overview_UnknownCompany_IsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.CreateCompanyAdminService().GetOverviewAsync(9999);

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
