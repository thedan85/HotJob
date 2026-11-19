using hotjob.Data;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Employer;
using hotjob.Services.Employer;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests;

public sealed class DemoSeederTests
{
    private static SeedOptions DemoSeed() => new()
    {
        Demo = new SeedDemoOptions
        {
            CompanyName = "Acme Corp",
            EmployerEmail = "employer@example.com",
            EmployerPassword = "Employer123!",
            EmployerName = "Demo Employer",
            CandidateEmail = "candidate@example.com",
            CandidatePassword = "Candidate123!",
            CandidateName = "Demo Candidate",
        }
    };

    [Fact]
    public async Task SeedAsync_CreatesApprovedCompanyWithActiveOwnerEmployer()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, DemoSeed(), NullLogger.Instance);

        var company = await fx.Db.CompanyProfiles.SingleAsync(c => c.CompanyName == "Acme Corp");
        Assert.Equal(CompanyStatus.Approved, company.Status);

        var user = await fx.UserManager.FindByEmailAsync("employer@example.com");
        Assert.NotNull(user);
        Assert.True(await fx.UserManager.IsInRoleAsync(user, AppRoles.Employer));

        var employer = await fx.Db.Employers.SingleAsync(e => e.UserId == user.Id);
        Assert.Equal(company.Id, employer.CompanyProfileId);
        Assert.Equal(MembershipStatus.Active, employer.MembershipStatus);
        Assert.True(employer.IsOwner);
    }

    [Fact]
    public async Task SeedAsync_CreatesPublishedJobWithCandidateResumeAndApplication()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, DemoSeed(), NullLogger.Instance);

        var job = await fx.Db.JobPostings
            .Include(j => j.JobSkills)
            .Include(j => j.CategoryMappings)
            .SingleAsync(j => j.Title == "Senior .NET Developer");
        Assert.Equal(JobStatus.Published, job.Status);
        Assert.NotEmpty(job.JobSkills);
        Assert.NotEmpty(job.CategoryMappings);

        var candidateUser = await fx.UserManager.FindByEmailAsync("candidate@example.com");
        Assert.NotNull(candidateUser);
        var candidate = await fx.Db.Candidates.SingleAsync(c => c.UserId == candidateUser.Id);
        var resume = await fx.Db.Resumes.SingleAsync(r => r.CandidateId == candidate.Id);

        var application = await fx.Db.Applications.SingleAsync(a => a.JobPostingId == job.Id && a.CandidateId == candidate.Id);
        Assert.Equal(resume.Id, application.ResumeId);
        Assert.Equal(ApplicationStatus.Pending, application.CurrentStatus);
    }

    [Fact]
    public async Task SeedAsync_DemoCandidate_IsDiscoverableAndOffersFilterSkills()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, DemoSeed(), NullLogger.Instance);

        var candidateUser = await fx.UserManager.FindByEmailAsync("candidate@example.com");
        Assert.NotNull(candidateUser);
        var candidate = await fx.Db.Candidates.SingleAsync(c => c.UserId == candidateUser.Id);
        Assert.True(candidate.IsDiscoverable);

        var employerUser = await fx.UserManager.FindByEmailAsync("employer@example.com");
        Assert.NotNull(employerUser);
        var search = new CandidateSearchService(fx.Db, new ApplicantRepository(fx.Db), TimeProvider.System);
        var skills = await search.GetFilterSkillsAsync(employerUser.Id);
        Assert.True(skills.Success, string.Join("; ", skills.Errors));
        Assert.Contains(skills.Data!, s => s.Name == "C#");
        Assert.Contains(skills.Data!, s => s.Name == "SQL");
    }

    [Fact]
    public async Task SeedAsync_SecondRun_IsIdempotent()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var seed = DemoSeed();

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, seed, NullLogger.Instance);
        var applicationCount = await fx.Db.Applications.CountAsync();
        var jobCount = await fx.Db.JobPostings.CountAsync();
        var candidateCount = await fx.Db.Candidates.CountAsync();
        var resumeCount = await fx.Db.Resumes.CountAsync();

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, seed, NullLogger.Instance);

        Assert.Equal(1, await fx.Db.CompanyProfiles.CountAsync(c => c.CompanyName == "Acme Corp"));
        Assert.Equal(1, await fx.Db.JobPostings.CountAsync(j => j.Title == "Senior .NET Developer"));
        Assert.Equal(1, await fx.Db.JobPostings.CountAsync(j => j.Title == "Frontend React Developer"));
        Assert.Equal(1, await fx.Db.JobPostings.CountAsync(j => j.Title == "Full Stack .NET Developer"));
        Assert.Equal(1, await fx.Db.JobPostings.CountAsync(j => j.Title == "QA Engineer"));
        Assert.Equal(jobCount, await fx.Db.JobPostings.CountAsync());
        Assert.Equal(candidateCount, await fx.Db.Candidates.CountAsync());
        Assert.Equal(resumeCount, await fx.Db.Resumes.CountAsync());
        Assert.Equal(applicationCount, await fx.Db.Applications.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_SeedsExtraJobsAndApplicationsForSwaggerFlows()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, DemoSeed(), NullLogger.Instance);

        var company = await fx.Db.CompanyProfiles.SingleAsync(c => c.CompanyName == "Acme Corp");
        var jobs = await fx.Db.JobPostings
            .Include(j => j.JobSkills)
            .Include(j => j.CategoryMappings)
            .Where(j => j.CompanyProfileId == company.Id)
            .ToListAsync();
        Assert.Contains(jobs, j => j.Title == "Frontend React Developer"
            && j.Status == JobStatus.Published
            && j.JobSkills.Count > 0
            && j.CategoryMappings.Count > 0);
        Assert.Contains(jobs, j => j.Title == "Full Stack .NET Developer" && j.Status == JobStatus.Published);
        Assert.Contains(jobs, j => j.Title == "QA Engineer" && j.Status == JobStatus.Draft);

        Assert.True(await fx.Db.Applications.AnyAsync(a => a.CurrentStatus == ApplicationStatus.Viewed));
        Assert.True(await fx.Db.Applications.AnyAsync(a => a.CurrentStatus == ApplicationStatus.Shortlisted));
        Assert.True(await fx.Db.Applications.AnyAsync(a => a.CurrentStatus == ApplicationStatus.Pending));
    }

    [Fact]
    public async Task SeedAsync_SeedsCatalogCompaniesWithOwnerEmployers()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, DemoSeed(), NullLogger.Instance);

        var catalogCount = await fx.Db.CompanyProfiles.CountAsync(c => c.CompanyName != "Acme Corp");
        Assert.True(catalogCount >= 50, $"Expected at least 50 catalog companies, got {catalogCount}.");

        Assert.True(await fx.Db.Skills.CountAsync() >= 50);
        Assert.True(await fx.Db.JobPostings.CountAsync() >= 50);
        Assert.True(await fx.Db.Candidates.CountAsync() >= 10);
        Assert.True(await fx.Db.Applications.CountAsync() >= 20);

        var approved = await fx.Db.CompanyProfiles.CountAsync(c => c.Status == CompanyStatus.Approved);
        Assert.Equal(await fx.Db.CompanyProfiles.CountAsync(), approved);

        var companiesWithoutOwner = await fx.Db.CompanyProfiles
            .Where(c => !c.Employers.Any(e => e.IsOwner && e.MembershipStatus == MembershipStatus.Active))
            .Select(c => c.CompanyName)
            .ToListAsync();
        Assert.Empty(companiesWithoutOwner);

        var companiesWithoutWebsite = await fx.Db.CompanyProfiles
            .Where(c => c.Website == null || c.Website == string.Empty)
            .Select(c => c.CompanyName)
            .ToListAsync();
        Assert.Empty(companiesWithoutWebsite);
    }

    [Fact]
    public async Task SeedAsync_CatalogSecondRun_IsIdempotent()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var seed = DemoSeed();

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, seed, NullLogger.Instance);
        var firstCount = await fx.Db.CompanyProfiles.CountAsync();
        var firstEmployers = await fx.Db.Employers.CountAsync();

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, seed, NullLogger.Instance);

        Assert.Equal(firstCount, await fx.Db.CompanyProfiles.CountAsync());
        Assert.Equal(firstEmployers, await fx.Db.Employers.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_EmptyDemoConfig_SkipsWithoutThrowing()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, new SeedOptions(), NullLogger.Instance);

        Assert.Equal(0, await fx.Db.CompanyProfiles.CountAsync());
        Assert.Equal(0, await fx.Db.JobPostings.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_SeedsReviewsForEveryCompany_Idempotent()
    {
        await using var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);
        var seed = DemoSeed();

        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, seed, NullLogger.Instance);

        var companyIds = await fx.Db.CompanyProfiles.Select(c => c.Id).ToListAsync();
        Assert.NotEmpty(companyIds);
        foreach (var companyId in companyIds)
        {
            Assert.True(
                await fx.Db.Reviews.AnyAsync(r => r.CompanyProfileId == companyId),
                $"Expected a review for company {companyId}.");
        }

        var reviewCount = await fx.Db.Reviews.CountAsync();
        await DemoSeeder.SeedAsync(fx.Db, fx.UserManager, fx.RoleManager, seed, NullLogger.Instance);
        Assert.Equal(reviewCount, await fx.Db.Reviews.CountAsync());
    }
}
