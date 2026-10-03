using hotjob.DTOs.Employer;
using hotjob.Data;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Repositories.Employer;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests;

public sealed class EmployerGuardTests
{
    private static async Task<(string UserId, int CompanyId)> SeedEmployerAsync(
        TestFixture fx, string email, CompanyStatus companyStatus)
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = $"Co {email}", Status = companyStatus };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        fx.Db.Employers.Add(new Employer
        {
            UserId = user.Id,
            CompanyProfileId = company.Id,
            FullName = "HR",
            MembershipStatus = MembershipStatus.Active
        });
        await fx.Db.SaveChangesAsync();
        return (user.Id, company.Id);
    }

    private static async Task<int> SeedApplicationAsync(TestFixture fx, int companyId)
    {
        var candidate = new Candidate { UserId = "cand", FullName = "Cand" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();
        var resume = new Resume { CandidateId = candidate.Id, Title = "CV" };
        fx.Db.Resumes.Add(resume);
        var job = new JobPosting
        {
            CompanyProfileId = companyId,
            Title = "Dev",
            Description = "d",
            Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        var app = new Application
        {
            JobPostingId = job.Id,
            ResumeId = resume.Id,
            CandidateId = candidate.Id
        };
        fx.Db.Applications.Add(app);
        await fx.Db.SaveChangesAsync();
        return app.Id;
    }

    [Fact]
    public async Task List_ApprovedCompany_Succeeds()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, companyId) = await SeedEmployerAsync(fx, "hr@example.com", CompanyStatus.Approved);
        var appId = await SeedApplicationAsync(fx, companyId);
        var svc = new ApplicantService(new ApplicantRepository(fx.Db), NullLogger<ApplicantService>.Instance);

        // ponytail: guard now loads Company; detach everything so the check reads fresh state.
        foreach (var e in fx.Db.ChangeTracker.Entries().ToArray()) e.State = EntityState.Detached;
        var result = await svc.GetDetailAsync(userId, appId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task List_RejectedCompany_Blocked()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, companyId) = await SeedEmployerAsync(fx, "hr@example.com", CompanyStatus.Rejected);
        var appId = await SeedApplicationAsync(fx, companyId);
        var svc = new ApplicantService(new ApplicantRepository(fx.Db), NullLogger<ApplicantService>.Instance);

        foreach (var e in fx.Db.ChangeTracker.Entries().ToArray()) e.State = EntityState.Detached;
        var result = await svc.GetDetailAsync(userId, appId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task UpdateStatus_RejectedCompany_Blocked()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, companyId) = await SeedEmployerAsync(fx, "hr@example.com", CompanyStatus.Rejected);
        var appId = await SeedApplicationAsync(fx, companyId);
        var svc = new ApplicantService(new ApplicantRepository(fx.Db), NullLogger<ApplicantService>.Instance);

        foreach (var e in fx.Db.ChangeTracker.Entries().ToArray()) e.State = EntityState.Detached;
        var result = await svc.UpdateStatusAsync(userId, appId, new UpdateStatusRequest { Status = ApplicationStatus.Viewed });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task CreateInterview_RejectedCompany_Blocked()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, companyId) = await SeedEmployerAsync(fx, "hr@example.com", CompanyStatus.Rejected);
        var appId = await SeedApplicationAsync(fx, companyId);
        var svc = new InterviewService(new ApplicantRepository(fx.Db), NullLogger<InterviewService>.Instance);

        foreach (var e in fx.Db.ChangeTracker.Entries().ToArray()) e.State = EntityState.Detached;
        var result = await svc.CreateAsync(userId, appId, new CreateInterviewRequest
        {
            InterviewDate = DateTime.UtcNow.AddDays(1),
            InterviewType = InterviewType.Online
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }
}
