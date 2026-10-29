using hotjob.Data;
using hotjob.DTOs.Job;
using hotjob.Models;
using hotjob.Models.Billing;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.BillingTests;

/// <summary>Effective-plan resolution, job quota enforcement and featured ordering (contract D6/D7).</summary>
public sealed class EntitlementQuotaTests
{
    private static JobService CreateService(TestFixture fx) => new(
        fx.Db,
        new JobPostingRepository(fx.Db),
        new EntitlementService(fx.Db, TimeProvider.System),
        new StubJobAlertService(),
        NullLogger<JobService>.Instance);

    private static EntitlementService Entitlements(TestFixture fx) =>
        new(fx.Db, TimeProvider.System);

    private static async Task<(string UserId, int SkillId, int CategoryId, CompanyProfile Company)>
        SeedEmployerAsync(TestFixture fx)
    {
        var user = await fx.SeedUserAsync("owner@example.com", "Str0ng!Pass", AppRoles.Employer);
        var company = new CompanyProfile
        {
            CompanyName = $"Co {Guid.NewGuid():N}",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(company);
        var skill = new Skill { Name = $"C# {Guid.NewGuid():N}", Category = "Backend" };
        var category = new JobCategory { Name = $"Backend {Guid.NewGuid():N}" };
        fx.Db.Skills.Add(skill);
        fx.Db.JobCategories.Add(category);
        await fx.Db.SaveChangesAsync();
        fx.Db.Employers.Add(new Employer
        {
            UserId = user.Id,
            CompanyProfileId = company.Id,
            FullName = "Owner",
            IsOwner = true,
            MembershipStatus = MembershipStatus.Active
        });
        await fx.Db.SaveChangesAsync();
        return (user.Id, skill.Id, category.Id, company);
    }

    private static CreateJobRequest NewRequest(int skillId, int categoryId, JobStatus status = JobStatus.Published) => new()
    {
        Title = "Senior .NET Developer",
        Description = "Build things.",
        SalaryMin = 1000,
        SalaryMax = 2000,
        ExperienceRequired = 3,
        JobType = JobType.FullTime,
        Status = status,
        Skills = [new() { SkillId = skillId, ImportanceWeight = 7 }],
        CategoryIds = [categoryId]
    };

    private static UpdateJobRequest UpdateRequest(int skillId, int categoryId, JobStatus status) => new()
    {
        Title = "Senior .NET Developer",
        Description = "Build things.",
        SalaryMin = 1000,
        SalaryMax = 2000,
        ExperienceRequired = 3,
        JobType = JobType.FullTime,
        Status = status,
        Skills = [new() { SkillId = skillId, ImportanceWeight = 7 }],
        CategoryIds = [categoryId]
    };

    private static async Task AssignPlanAsync(TestFixture fx, int companyId, string code, DateTime? expiresAt)
    {
        var plan = await fx.Db.Plans.AsNoTracking().SingleAsync(p => p.Code == code);
        var company = await fx.Db.CompanyProfiles.SingleAsync(c => c.Id == companyId);
        company.PlanId = plan.Id;
        company.PlanStartedAt = DateTime.UtcNow;
        company.PlanExpiresAt = expiresAt;
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task FreePlan_BlocksSecondActiveJob_AndClosingFreesASlot()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var (userId, skillId, categoryId, _) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);

        var first = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        Assert.True(first.Success, string.Join("; ", first.Errors));

        var blocked = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        Assert.False(blocked.Success);
        Assert.Equal(ServiceErrorType.Conflict, blocked.ErrorType);
        Assert.Equal(
            "You have used all 1/1 job slots on your plan. Upgrade to post more.",
            blocked.Errors.Single());

        await svc.DeleteOrCloseAsync(userId, first.Data!.Id);
        var afterClose = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        Assert.True(afterClose.Success, string.Join("; ", afterClose.Errors));
    }

    [Fact]
    public async Task DraftJobs_CountTowardsQuota()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var (userId, skillId, categoryId, _) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);

        var draft = await svc.CreateAsync(userId, NewRequest(skillId, categoryId, JobStatus.Draft));
        var second = await svc.CreateAsync(userId, NewRequest(skillId, categoryId, JobStatus.Draft));

        Assert.True(draft.Success);
        Assert.False(second.Success);
        Assert.Equal(ServiceErrorType.Conflict, second.ErrorType);
    }

    [Fact]
    public async Task ExpiredPaidPlan_FallsBackToFreeQuota()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var (userId, skillId, categoryId, company) = await SeedEmployerAsync(fx);
        await AssignPlanAsync(fx, company.Id, Plan.GrowthCode, DateTime.UtcNow.AddDays(-1));
        var svc = CreateService(fx);

        var entitlement = await Entitlements(fx).GetForUserAsync(userId);
        Assert.NotNull(entitlement);
        Assert.True(entitlement!.IsFree);
        Assert.Equal(1, entitlement.JobQuota);

        Assert.True((await svc.CreateAsync(userId, NewRequest(skillId, categoryId))).Success);
        var second = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        Assert.Equal(ServiceErrorType.Conflict, second.ErrorType);
    }

    [Fact]
    public async Task PaidPlan_UsesItsQuota()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var (userId, skillId, categoryId, company) = await SeedEmployerAsync(fx);
        await AssignPlanAsync(fx, company.Id, Plan.StarterCode, DateTime.UtcNow.AddDays(30));
        var svc = CreateService(fx);

        for (var index = 0; index < 5; index++)
        {
            var created = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
            Assert.True(created.Success, string.Join("; ", created.Errors));
        }

        var blocked = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        Assert.Equal(ServiceErrorType.Conflict, blocked.ErrorType);
        Assert.Contains("5/5 job slots", blocked.Errors.Single());
    }

    [Fact]
    public async Task ReopeningAJob_IsBlockedWhenQuotaIsFull()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var (userId, skillId, categoryId, _) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);

        var closedJob = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        await svc.DeleteOrCloseAsync(userId, closedJob.Data!.Id);
        var activeJob = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        Assert.True(activeJob.Success);

        var reopen = await svc.UpdateAsync(
            userId, closedJob.Data.Id, UpdateRequest(skillId, categoryId, JobStatus.Published));
        Assert.False(reopen.Success);
        Assert.Equal(ServiceErrorType.Conflict, reopen.ErrorType);

        await svc.DeleteOrCloseAsync(userId, activeJob.Data!.Id);
        var reopened = await svc.UpdateAsync(
            userId, closedJob.Data.Id, UpdateRequest(skillId, categoryId, JobStatus.Published));
        Assert.True(reopened.Success, string.Join("; ", reopened.Errors));
        Assert.Equal(JobStatus.Published, reopened.Data!.Status);
    }

    [Fact]
    public async Task Entitlement_ReportsUsageAndRemainingSlots()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var (userId, skillId, categoryId, _) = await SeedEmployerAsync(fx);
        await CreateService(fx).CreateAsync(userId, NewRequest(skillId, categoryId));

        var entitlement = await Entitlements(fx).GetForUserAsync(userId);

        Assert.NotNull(entitlement);
        Assert.Equal(1, entitlement!.ActiveJobCount);
        Assert.Equal(0, entitlement.RemainingSlots);
        Assert.True(entitlement.CanAddJob is false);
        Assert.True(entitlement.IsOwner);
        Assert.Equal(Plan.FreeCode, entitlement.PlanCode);
    }

    [Fact]
    public async Task FeaturedPublishedJobs_SortBeforeNewestJobs()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var (_, _, _, company) = await SeedEmployerAsync(fx);
        var now = DateTime.UtcNow;

        var olderFeatured = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Featured",
            Description = "Build",
            Status = JobStatus.Published,
            CreatedAt = now.AddDays(-10),
            FeaturedUntil = now.AddDays(2)
        };
        var newerPlain = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Plain",
            Description = "Build",
            Status = JobStatus.Published,
            CreatedAt = now
        };
        fx.Db.JobPostings.AddRange(olderFeatured, newerPlain);
        await fx.Db.SaveChangesAsync();

        var (items, total) = await new JobPostingRepository(fx.Db)
            .BrowsePublishedAsync(null, null, null, null, null, 1, 20);

        Assert.Equal(2, total);
        Assert.Equal("Featured", items[0].Title);
        Assert.Equal("Plain", items[1].Title);
    }
}
