using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.Support;

/// <summary>Small builders for resume/job/application graphs used by the ML tests.</summary>
internal static class MatchTestData
{
    public static async Task<Skill> GetOrCreateSkillAsync(TestFixture fx, string name)
    {
        var existing = await fx.Db.Skills.FirstOrDefaultAsync(skill => skill.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var skill = new Skill { Name = name, Category = "Test" };
        fx.Db.Skills.Add(skill);
        await fx.Db.SaveChangesAsync();
        return skill;
    }

    public static async Task<(Candidate Candidate, Resume Resume)> CreateCandidateAsync(
        TestFixture fx,
        string email,
        params (string Name, int Years)[] skills)
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = "Match Candidate" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();

        var resume = new Resume { CandidateId = candidate.Id, Title = "Primary CV", IsPrimary = true };
        foreach (var (name, years) in skills)
        {
            var skill = await GetOrCreateSkillAsync(fx, name);
            resume.ResumeSkills.Add(new ResumeSkill { SkillId = skill.Id, YearsOfExperience = years });
        }

        fx.Db.Resumes.Add(resume);
        await fx.Db.SaveChangesAsync();
        return (candidate, resume);
    }

    public static async Task<CompanyProfile> CreateCompanyAsync(TestFixture fx)
    {
        var company = new CompanyProfile
        {
            CompanyName = $"ML Co {Guid.NewGuid():N}",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        return company;
    }

    public static async Task<Employer> CreateEmployerAsync(TestFixture fx, int companyId, string email)
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Employer);
        var employer = new Employer
        {
            UserId = user.Id,
            CompanyProfileId = companyId,
            FullName = "Owner",
            IsOwner = true,
            MembershipStatus = MembershipStatus.Active
        };
        fx.Db.Employers.Add(employer);
        await fx.Db.SaveChangesAsync();
        return employer;
    }

    public static async Task<JobPosting> CreateJobAsync(
        TestFixture fx,
        int companyId,
        IReadOnlyList<(int SkillId, double Weight)> skills,
        int experienceRequired = 1,
        JobStatus status = JobStatus.Published,
        string title = "Test role")
    {
        var job = new JobPosting
        {
            CompanyProfileId = companyId,
            Title = title,
            Description = "Test job description.",
            ExperienceRequired = experienceRequired,
            JobType = JobType.FullTime,
            Status = status,
            JobSkills = skills
                .Select(skill => new JobSkill { SkillId = skill.SkillId, ImportanceWeight = skill.Weight })
                .ToList()
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        return job;
    }

    public static async Task<Application> CreateApplicationAsync(
        TestFixture fx,
        JobPosting job,
        Resume resume,
        Candidate candidate,
        ApplicationStatus status,
        string? latestNote = null)
    {
        var application = new Application
        {
            JobPostingId = job.Id,
            ResumeId = resume.Id,
            CandidateId = candidate.Id,
            CurrentStatus = status
        };
        fx.Db.Applications.Add(application);
        await fx.Db.SaveChangesAsync();

        if (latestNote is not null)
        {
            fx.Db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.Id,
                Status = status,
                Notes = latestNote,
                ChangedAt = DateTime.UtcNow
            });
            await fx.Db.SaveChangesAsync();
        }

        return application;
    }
}
