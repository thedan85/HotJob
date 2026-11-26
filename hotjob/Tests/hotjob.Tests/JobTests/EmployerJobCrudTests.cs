using hotjob.Data;
using hotjob.DTOs.Job;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Common;
using hotjob.Services.Billing;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.JobTests;

/// <summary>Employer job posting CRUD over the real service + repository (SQLite in-memory).</summary>
public sealed class EmployerJobCrudTests
{
    private static JobService CreateService(TestFixture fx) =>
        new(fx.Db, new JobPostingRepository(fx.Db), new EntitlementService(fx.Db, TimeProvider.System), new StubJobAlertService(), NullLogger<JobService>.Instance);

    private static async Task<(string UserId, int SkillId, int CategoryId)> SeedEmployerAsync(
        TestFixture fx, string email = "owner@example.com")
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = $"Co {Guid.NewGuid():N}", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        fx.Db.Employers.Add(new Employer
        {
            UserId = user.Id,
            CompanyProfileId = company.Id,
            FullName = "Owner",
            IsOwner = true,
            MembershipStatus = MembershipStatus.Active
        });
        var skill = new Skill { Name = $"C# {Guid.NewGuid():N}", Category = "Backend" };
        fx.Db.Skills.Add(skill);
        var category = new JobCategory { Name = $"Backend {Guid.NewGuid():N}" };
        fx.Db.JobCategories.Add(category);
        await fx.Db.SaveChangesAsync();
        return (user.Id, skill.Id, category.Id);
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

    [Fact]
    public async Task Create_Get_Update_Roundtrip()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);

        var created = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        Assert.True(created.Success, string.Join("; ", created.Errors));
        Assert.True(created.Data!.Id > 0);
        Assert.Equal(JobStatus.Draft, created.Data.Status);
        Assert.Single(created.Data.Skills);
        Assert.Single(created.Data.Categories);

        var fetched = await svc.GetAsync(userId, created.Data.Id);
        Assert.True(fetched.Success, string.Join("; ", fetched.Errors));
        Assert.Equal("Senior .NET Developer", fetched.Data!.Title);

        var update = new UpdateJobRequest
        {
            Title = "Lead .NET Developer",
            Description = "Build things.",
            SalaryMin = 1000,
            SalaryMax = 3000,
            ExperienceRequired = 3,
            JobType = JobType.FullTime,
            Status = JobStatus.Published,
            Skills = new List<JobSkillInput> { new() { SkillId = skillId, ImportanceWeight = 7 } },
            CategoryIds = new List<int> { categoryId }
        };
        var updated = await svc.UpdateAsync(userId, created.Data.Id, update);
        Assert.True(updated.Success, string.Join("; ", updated.Errors));
        Assert.Equal("Lead .NET Developer", updated.Data!.Title);
        Assert.Equal(JobStatus.PendingApproval, updated.Data.Status);
        Assert.Equal(3000m, updated.Data.SalaryMax);

        await fx.ApproveJobAsync(created.Data.Id);
        var published = await svc.UpdateAsync(userId, created.Data.Id, update);
        Assert.True(published.Success, string.Join("; ", published.Errors));
        Assert.Equal(JobStatus.Published, published.Data!.Status);
    }

    [Fact]
    public async Task Delete_Draft_RemovesRow()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);
        var created = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));

        var deleted = await svc.DeleteOrCloseAsync(userId, created.Data!.Id);

        Assert.True(deleted.Success, string.Join("; ", deleted.Errors));
        Assert.False(deleted.Data!.Closed);
        var afterDelete = await svc.GetAsync(userId, created.Data.Id);
        Assert.False(afterDelete.Success);
        Assert.Equal(ServiceErrorType.NotFound, afterDelete.ErrorType);
    }

    [Fact]
    public async Task Delete_Published_ClosesInsteadOfRemoving()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);
        var created = await svc.CreateAsync(userId, NewRequest(skillId, categoryId, JobStatus.Published));

        var closed = await svc.DeleteOrCloseAsync(userId, created.Data!.Id);

        Assert.True(closed.Success, string.Join("; ", closed.Errors));
        Assert.True(closed.Data!.Closed);
        Assert.Equal(JobStatus.Closed, closed.Data.Status);
        var stillThere = await svc.GetAsync(userId, created.Data.Id);
        Assert.True(stillThere.Success);
        Assert.Equal(JobStatus.Closed, stillThere.Data!.Status);
    }

    [Fact]
    public async Task Update_PublishedJobWithApplications_CannotGoBackToDraft()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);
        var jobId = await CreatePublishedJobAsync(fx, svc, userId, skillId, categoryId);
        var applicationId = await AddApplicantAsync(fx, jobId);

        var result = await svc.UpdateAsync(userId, jobId, UpdateRequest(skillId, categoryId, JobStatus.Draft));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Equal(JobStatus.Published, await StoredStatusAsync(fx, jobId));
        Assert.True(await fx.Db.Applications.AsNoTracking().AnyAsync(a => a.Id == applicationId));
    }

    [Fact]
    public async Task Update_PublishedJobWithoutApplications_CanGoBackToDraft()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);
        var jobId = await CreatePublishedJobAsync(fx, svc, userId, skillId, categoryId);

        var result = await svc.UpdateAsync(userId, jobId, UpdateRequest(skillId, categoryId, JobStatus.Draft));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(JobStatus.Draft, result.Data!.Status);
    }

    [Fact]
    public async Task Update_ClosedJobWithApplications_CannotReopenAsDraft()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);
        var jobId = await CreatePublishedJobAsync(fx, svc, userId, skillId, categoryId);
        await AddApplicantAsync(fx, jobId);
        Assert.True((await svc.DeleteOrCloseAsync(userId, jobId)).Data!.Closed);

        var result = await svc.UpdateAsync(userId, jobId, UpdateRequest(skillId, categoryId, JobStatus.Draft));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Equal(JobStatus.Closed, await StoredStatusAsync(fx, jobId));
    }

    [Fact]
    public async Task Delete_DraftWithApplications_ClosesAndKeepsApplications()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, skillId, categoryId) = await SeedEmployerAsync(fx);
        var svc = CreateService(fx);
        var created = await svc.CreateAsync(userId, NewRequest(skillId, categoryId));
        // A draft that already has an applicant: data from before such moves were blocked.
        var applicationId = await AddApplicantAsync(fx, created.Data!.Id);

        var result = await svc.DeleteOrCloseAsync(userId, created.Data.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.Closed);
        Assert.Equal(JobStatus.Closed, result.Data.Status);
        Assert.Equal(JobStatus.Closed, await StoredStatusAsync(fx, created.Data.Id));
        var application = await fx.Db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
        Assert.Equal(ApplicationStatus.Rejected, application.CurrentStatus);
        Assert.True(await fx.Db.ApplicationStatusHistories.AsNoTracking()
            .AnyAsync(h => h.ApplicationId == applicationId && h.Status == ApplicationStatus.Rejected));
    }

    private static UpdateJobRequest UpdateRequest(int skillId, int categoryId, JobStatus status) => new()
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

    /// <summary>Create → admin approval → publish, the only way a job goes live.</summary>
    private static async Task<int> CreatePublishedJobAsync(
        TestFixture fx, JobService svc, string userId, int skillId, int categoryId)
    {
        var created = await svc.CreateAsync(userId, NewRequest(skillId, categoryId, JobStatus.Published));
        Assert.True(created.Success, string.Join("; ", created.Errors));
        await fx.ApproveJobAsync(created.Data!.Id);
        var published = await svc.UpdateAsync(userId, created.Data.Id, UpdateRequest(skillId, categoryId, JobStatus.Published));
        Assert.True(published.Success, string.Join("; ", published.Errors));
        Assert.Equal(JobStatus.Published, published.Data!.Status);
        return created.Data.Id;
    }

    private static async Task<int> AddApplicantAsync(TestFixture fx, int jobId)
    {
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, $"cand-{Guid.NewGuid():N}@example.com");
        var job = await fx.Db.JobPostings.SingleAsync(j => j.Id == jobId);
        var application = await MatchTestData.CreateApplicationAsync(fx, job, resume, candidate, ApplicationStatus.Pending);
        return application.Id;
    }

    private static Task<JobStatus> StoredStatusAsync(TestFixture fx, int jobId) =>
        fx.Db.JobPostings.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.Status).SingleAsync();

    [Fact]
    public async Task Create_Unauthorized_WhenNoActiveEmployer()
    {
        await using var fx = await TestFixture.CreateAsync();
        var stranger = await fx.SeedUserAsync("stranger@example.com");
        var svc = CreateService(fx);

        var result = await svc.CreateAsync(stranger.Id, NewRequest(1, 1));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }
}
