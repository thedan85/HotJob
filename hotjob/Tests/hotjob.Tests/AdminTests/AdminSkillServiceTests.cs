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

    [Fact]
    public async Task GetSkillAsync_Missing_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).GetSkillAsync(9999);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
