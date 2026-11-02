using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace hotjob.Tests.AdminTests;

public sealed class AdminSkillServiceTests
{
    private static AdminService CreateService(TestFixture fx) =>
        new(fx.Db, fx.UserManager, fx.RoleManager);

    private static async Task<Skill> SeedSkillAsync(
        TestFixture fx,
        string name,
        string? category = null)
    {
        var skill = new Skill { Name = name, Category = category };
        fx.Db.Skills.Add(skill);
        await fx.Db.SaveChangesAsync();
        return skill;
    }

    private static async Task AddUsageAsync(TestFixture fx, int skillId)
    {
        var user = await fx.SeedUserAsync(
            email: $"{Guid.NewGuid():N}@example.com",
            role: AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = "Test Candidate" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();

        var resume = new Resume { CandidateId = candidate.Id, Title = "CV" };
        var company = new CompanyProfile
        {
            CompanyName = $"Company {Guid.NewGuid():N}",
            Status = CompanyStatus.Approved
        };
        fx.Db.Resumes.Add(resume);
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        fx.Db.ResumeSkills.Add(new ResumeSkill
        {
            ResumeId = resume.Id,
            SkillId = skillId
        });

        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Developer",
            Description = "Build",
            Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();

        fx.Db.JobSkills.Add(new JobSkill
        {
            JobPostingId = job.Id,
            SkillId = skillId
        });
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSkillsAsync_FiltersAndOrdersByName()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedSkillAsync(fx, "Zeta", "Backend");
        await SeedSkillAsync(fx, "Alpha", "Backend");
        await SeedSkillAsync(fx, "Beta", "Frontend");

        var result = await CreateService(fx).GetSkillsAsync("a", "Backend", 1, 50);

        Assert.True(result.Success);
        Assert.Equal(new[] { "Alpha", "Zeta" }, result.Data!.Items.Select(skill => skill.Name));
        Assert.Equal(2, result.Data.TotalCount);
    }

    [Fact]
    public async Task GetSkillsAsync_ClampsPageAndPageSize()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedSkillAsync(fx, "C#");

        var result = await CreateService(fx).GetSkillsAsync(null, null, 0, 1000);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(200, result.Data.PageSize);
    }

    [Fact]
    public async Task GetSkillAsync_ReturnsUsageCounts()
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "C#");
        await AddUsageAsync(fx, skill.Id);

        var result = await CreateService(fx).GetSkillAsync(skill.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.ResumeCount);
        Assert.Equal(1, result.Data.JobCount);
    }

    [Theory]
    [InlineData(1073741825, 20)]
    [InlineData(int.MaxValue, 200)]
    public async Task GetSkillsAsync_LargePage_ReturnsEmptyWithoutOverflow(int page, int pageSize)
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedSkillAsync(fx, "C#");

        var result = await CreateService(fx).GetSkillsAsync(null, null, page, pageSize);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.TotalCount);
        Assert.Equal(page, result.Data.Page);
        Assert.Empty(result.Data.Items);
    }

    [Fact]
    public async Task GetSkillAsync_Missing_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).GetSkillAsync(9999);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task CreateSkillAsync_ValidRequest_TrimsAndReturnsSkill()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).CreateSkillAsync(
            new CreateSkillRequest { Name = "  Rust  ", Category = "  Backend  " });

        Assert.True(result.Success);
        Assert.Equal("Rust", result.Data!.Name);
        Assert.Equal("Backend", result.Data.Category);
        Assert.Equal(0, result.Data.ResumeCount);
        Assert.Equal(0, result.Data.JobCount);
    }

    [Fact]
    public async Task CreateSkillAsync_DuplicateName_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedSkillAsync(fx, "C#");

        var result = await CreateService(fx).CreateSkillAsync(
            new CreateSkillRequest { Name = "C#" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task CreateSkillAsync_CaseInsensitiveDuplicate_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedSkillAsync(fx, "C#");

        var result = await CreateService(fx).CreateSkillAsync(
            new CreateSkillRequest { Name = "c#" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task CreateSkillAsync_WhitespaceName_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).CreateSkillAsync(
            new CreateSkillRequest { Name = "   " });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task CreateSkillAsync_NameTooLong_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).CreateSkillAsync(
            new CreateSkillRequest { Name = new string('a', 101) });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task UpdateSkillAsync_Rename_KeepsResumeAndJobLinks()
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "C#");
        await AddUsageAsync(fx, skill.Id);

        var result = await CreateService(fx).UpdateSkillAsync(
            skill.Id,
            new UpdateSkillRequest { Name = "C# / .NET", Category = "Backend" });

        Assert.True(result.Success);
        Assert.Equal("C# / .NET", result.Data!.Name);
        Assert.Equal(1, result.Data.ResumeCount);
        Assert.Equal(1, result.Data.JobCount);
        Assert.Equal(1, await fx.Db.ResumeSkills.CountAsync());
        Assert.Equal(1, await fx.Db.JobSkills.CountAsync());
    }

    [Fact]
    public async Task UpdateSkillAsync_DuplicateName_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var first = await SeedSkillAsync(fx, "C#");
        await SeedSkillAsync(fx, "SQL");

        var result = await CreateService(fx).UpdateSkillAsync(
            first.Id,
            new UpdateSkillRequest { Name = "SQL" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task UpdateSkillAsync_SameNameDifferentCase_AllowsUpdate()
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "C#");

        var result = await CreateService(fx).UpdateSkillAsync(
            skill.Id,
            new UpdateSkillRequest { Name = "c#" });

        Assert.True(result.Success);
        Assert.Equal("c#", result.Data!.Name);
    }

    [Fact]
    public async Task DeleteSkillAsync_UnusedSkill_Deletes()
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "Rust");

        var result = await CreateService(fx).DeleteSkillAsync(skill.Id);

        Assert.True(result.Success);
        Assert.Null(await fx.Db.Skills.FindAsync(skill.Id));
    }

    [Fact]
    public async Task DeleteSkillAsync_SkillInUse_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "C#");
        await AddUsageAsync(fx, skill.Id);

        var result = await CreateService(fx).DeleteSkillAsync(skill.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.NotNull(await fx.Db.Skills.FindAsync(skill.Id));
        Assert.Equal(1, await fx.Db.ResumeSkills.CountAsync());
        Assert.Equal(1, await fx.Db.JobSkills.CountAsync());
    }

    [Theory]
    [InlineData("create", false)]
    [InlineData("update", false)]
    [InlineData("delete", false)]
    [InlineData("create", true)]
    [InlineData("update", true)]
    [InlineData("delete", true)]
    public async Task SkillWrite_UnrelatedSaveFailure_Propagates(string operation, bool concurrencyFailure)
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "Existing");
        DbUpdateException failure = concurrencyFailure
            ? new DbUpdateConcurrencyException("The row changed concurrently.")
            : new DbUpdateException("Storage unavailable.", new IOException("Disk failure."));
        await using var db = CreateInterceptedDb(fx, () => throw failure);
        var service = new AdminService(db, fx.UserManager, fx.RoleManager);

        var thrown = await Record.ExceptionAsync(() => WriteSkillAsync(service, operation, skill.Id));

        Assert.Same(failure, thrown);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public async Task SkillWrite_RacingDuplicate_ReturnsConflict(string operation)
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "Existing");
        await using var db = CreateInterceptedDb(fx, async () =>
        {
            await SeedSkillAsync(fx, "Rust");
        });
        var service = new AdminService(db, fx.UserManager, fx.RoleManager);

        var result = await WriteSkillAsync(service, operation, skill.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Equal(1, await fx.Db.Skills.CountAsync(row => row.Name == "Rust"));
        Assert.Equal("Existing", (await fx.Db.Skills.AsNoTracking().SingleAsync(row => row.Id == skill.Id)).Name);
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("job")]
    public async Task DeleteSkillAsync_ReferenceInsertedAfterCounts_ReturnsConflict(string link)
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = await SeedSkillAsync(fx, "C#");
        var (_, resume) = await MatchTestData.CreateCandidateAsync(fx, "race@example.com");
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, []);
        await using var db = CreateInterceptedDb(fx, async () =>
        {
            if (link == "resume")
                fx.Db.ResumeSkills.Add(new ResumeSkill { ResumeId = resume.Id, SkillId = skill.Id });
            else
                fx.Db.JobSkills.Add(new JobSkill { JobPostingId = job.Id, SkillId = skill.Id });
            await fx.Db.SaveChangesAsync();
        });
        var service = new AdminService(db, fx.UserManager, fx.RoleManager);

        var result = await service.DeleteSkillAsync(skill.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.True(await fx.Db.Skills.AsNoTracking().AnyAsync(row => row.Id == skill.Id));
        Assert.Equal(link == "resume" ? 1 : 0, await fx.Db.ResumeSkills.CountAsync());
        Assert.Equal(link == "job" ? 1 : 0, await fx.Db.JobSkills.CountAsync());
    }

    private static ApplicationDbContext CreateInterceptedDb(TestFixture fx, Func<Task> beforeSave) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(fx.Connection)
            .AddInterceptors(new BeforeSaveInterceptor(beforeSave))
            .Options);

    private static async Task<ServiceResult> WriteSkillAsync(AdminService service, string operation, int skillId) =>
        operation switch
        {
            "create" => await service.CreateSkillAsync(new CreateSkillRequest { Name = "Rust" }),
            "update" => await service.UpdateSkillAsync(skillId, new UpdateSkillRequest { Name = "Rust" }),
            "delete" => await service.DeleteSkillAsync(skillId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private sealed class BeforeSaveInterceptor(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await beforeSave();
            return result;
        }
    }
}
