using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.Models;
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

/// <summary>
/// Employer ATS (2.3): applicant list/search (2.3.1), resume detail (2.3.2),
/// status review (2.3.3), interview scheduling (2.3.4) and candidate
/// notifications (2.3.5), driven through the real services + repository on
/// SQLite in-memory.
/// </summary>
public sealed class EmployerAtsTests
{
    private static ApplicantService Applicants(TestFixture fx) =>
        new(new ApplicantRepository(fx.Db), NullLogger<ApplicantService>.Instance);

    private static InterviewService Interviews(TestFixture fx) =>
        new(new ApplicantRepository(fx.Db), NullLogger<InterviewService>.Instance);

    private static NotificationService Notifier(TestFixture fx) =>
        new(new ApplicantRepository(fx.Db));

    // The guard loads Employer.Company through a tracked query; drop the seeded
    // state so the service reads fresh rows instead of the fixture's identity map.
    private static void Detach(TestFixture fx)
    {
        foreach (var entry in fx.Db.ChangeTracker.Entries().ToArray())
            entry.State = EntityState.Detached;
    }

    private static async Task<(string OwnerUserId, int CompanyId)> SeedEmployerAsync(
        TestFixture fx, string email)
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = $"Co {email}", Status = CompanyStatus.Approved };
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

    private static async Task<(int ApplicationId, string CandidateUserId, int JobId)> SeedApplicationAsync(
        TestFixture fx,
        int companyId,
        string candidateEmail = "cand@example.com",
        string candidateName = "Cand",
        ApplicationStatus status = ApplicationStatus.Pending,
        bool withResumeDetail = false)
    {
        var candidateUser = await fx.SeedUserAsync(candidateEmail, "Str0ng!Pass", AppRoles.Candidate);
        var candidate = new Candidate { UserId = candidateUser.Id, FullName = candidateName };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();

        var resume = new Resume { CandidateId = candidate.Id, Title = $"{candidateName} CV" };
        fx.Db.Resumes.Add(resume);
        await fx.Db.SaveChangesAsync();

        if (withResumeDetail)
        {
            var skill = new Skill { Name = $"C# {Guid.NewGuid():N}", Category = "Backend" };
            fx.Db.Skills.Add(skill);
            await fx.Db.SaveChangesAsync();
            fx.Db.Educations.Add(new Education { ResumeId = resume.Id, SchoolName = "HUST", Major = "CS" });
            fx.Db.WorkExperiences.Add(new WorkExperience { ResumeId = resume.Id, CompanyName = "Acme", JobTitle = "Dev" });
            fx.Db.ResumeSkills.Add(new ResumeSkill { ResumeId = resume.Id, SkillId = skill.Id, YearsOfExperience = 3 });
            await fx.Db.SaveChangesAsync();
        }

        var job = new JobPosting
        {
            CompanyProfileId = companyId,
            Title = $"Dev {Guid.NewGuid():N}",
            Description = "d",
            Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();

        var application = new Application
        {
            JobPostingId = job.Id,
            ResumeId = resume.Id,
            CandidateId = candidate.Id,
            CurrentStatus = status
        };
        fx.Db.Applications.Add(application);
        await fx.Db.SaveChangesAsync();
        return (application.Id, candidateUser.Id, job.Id);
    }

    // 2.3.1 search applicants ------------------------------------------------

    [Fact]
    public async Task List_ScopesToOwnCompany()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyA) = await SeedEmployerAsync(fx, "a@example.com");
        var (_, companyB) = await SeedEmployerAsync(fx, "b@example.com");
        await SeedApplicationAsync(fx, companyA, "a-cand@example.com", "Alice");
        await SeedApplicationAsync(fx, companyB, "b-cand@example.com", "Bob");
        Detach(fx);

        var result = await Applicants(fx).ListAsync(owner, new ApplicantListQuery());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(1, result.Data!.TotalCount);
        Assert.Equal("Alice", result.Data.Items[0].CandidateFullName);
    }

    [Fact]
    public async Task List_Search_MatchesNameOrEmail()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        await SeedApplicationAsync(fx, companyId, "alice@example.com", "Alice Nguyen");
        await SeedApplicationAsync(fx, companyId, "bob@example.com", "Bob Tran");
        Detach(fx);

        var byName = await Applicants(fx).ListAsync(owner, new ApplicantListQuery { Search = "alice" });
        var byEmail = await Applicants(fx).ListAsync(owner, new ApplicantListQuery { Search = "bob@" });
        var none = await Applicants(fx).ListAsync(owner, new ApplicantListQuery { Search = "zzznomatch" });

        Assert.Equal(1, byName.Data!.TotalCount);
        Assert.Equal("Alice Nguyen", byName.Data.Items[0].CandidateFullName);
        Assert.Equal(1, byEmail.Data!.TotalCount);
        Assert.Equal(0, none.Data!.TotalCount);
    }

    [Fact]
    public async Task List_StatusFilter_AndPaging()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        await SeedApplicationAsync(fx, companyId, "p1@example.com", "P1", ApplicationStatus.Pending);
        await SeedApplicationAsync(fx, companyId, "p2@example.com", "P2", ApplicationStatus.Pending);
        await SeedApplicationAsync(fx, companyId, "v1@example.com", "V1", ApplicationStatus.Viewed);
        Detach(fx);

        var pending = await Applicants(fx).ListAsync(owner, new ApplicantListQuery { Status = ApplicationStatus.Pending });
        var page = await Applicants(fx).ListAsync(owner, new ApplicantListQuery { Page = 1, PageSize = 2 });

        Assert.Equal(2, pending.Data!.TotalCount);
        Assert.Equal(3, page.Data!.TotalCount);
        Assert.Equal(2, page.Data.Items.Count);
        Assert.Equal(2, page.Data.TotalPages);
    }

    [Fact]
    public async Task List_NoEmployer_Unauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var stranger = await fx.SeedUserAsync("nobody@example.com");

        var result = await Applicants(fx).ListAsync(stranger.Id, new ApplicantListQuery());

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    // 2.3.2 view applicant resume -------------------------------------------

    [Fact]
    public async Task GetDetail_ReturnsResume_Education_Experience_Skills()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, _, _) = await SeedApplicationAsync(
            fx, companyId, "cand@example.com", "Cand", ApplicationStatus.Pending, withResumeDetail: true);
        Detach(fx);

        var result = await Applicants(fx).GetDetailAsync(owner, appId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var detail = result.Data!;
        Assert.Equal("Cand", detail.CandidateFullName);
        Assert.Equal("cand@example.com", detail.CandidateEmail);
        Assert.Equal("Cand CV", detail.Resume.Title);
        Assert.Single(detail.Resume.Educations);
        Assert.Single(detail.Resume.WorkExperiences);
        Assert.Single(detail.Resume.Skills);
        Assert.Equal(3, detail.Resume.Skills[0].YearsOfExperience);
    }

    [Fact]
    public async Task GetDetail_ForeignApplication_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, _) = await SeedEmployerAsync(fx, "hr@example.com");
        var (_, otherCompany) = await SeedEmployerAsync(fx, "other@example.com");
        var (foreignAppId, _, _) = await SeedApplicationAsync(fx, otherCompany);
        Detach(fx);

        var result = await Applicants(fx).GetDetailAsync(owner, foreignAppId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    // 2.3.3 review/approve resume -------------------------------------------

    [Theory]
    [InlineData(ApplicationStatus.Pending, ApplicationStatus.Viewed, true, null)]
    [InlineData(ApplicationStatus.Viewed, ApplicationStatus.Shortlisted, true, null)]
    [InlineData(ApplicationStatus.Shortlisted, ApplicationStatus.Interviewing, true, null)]
    [InlineData(ApplicationStatus.Interviewing, ApplicationStatus.Offered, true, null)]
    [InlineData(ApplicationStatus.Viewed, ApplicationStatus.Rejected, true, null)]
    [InlineData(ApplicationStatus.Viewed, ApplicationStatus.Pending, false, ServiceErrorType.Conflict)]
    [InlineData(ApplicationStatus.Offered, ApplicationStatus.Rejected, false, ServiceErrorType.Conflict)]
    [InlineData(ApplicationStatus.Pending, ApplicationStatus.Withdrawn, false, ServiceErrorType.Validation)]
    public async Task UpdateStatus_EnforcesForwardOnlyTransitions(
        ApplicationStatus from, ApplicationStatus to, bool expectedSuccess, ServiceErrorType? expectedError)
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, _, _) = await SeedApplicationAsync(fx, companyId, status: from);
        Detach(fx);

        var result = await Applicants(fx).UpdateStatusAsync(owner, appId, new UpdateStatusRequest { Status = to });

        Assert.Equal(expectedSuccess, result.Success);
        if (expectedSuccess)
            Assert.Equal(to, result.Data!.CurrentStatus);
        else
            Assert.Equal(expectedError, result.ErrorType);
    }

    [Fact]
    public async Task UpdateStatus_WritesHistoryAndNotifiesCandidate()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, candidateUserId, _) = await SeedApplicationAsync(fx, companyId);
        Detach(fx);

        var result = await Applicants(fx).UpdateStatusAsync(owner, appId, new UpdateStatusRequest
        {
            Status = ApplicationStatus.Viewed,
            Notes = "looks good"
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(ApplicationStatus.Viewed, result.Data!.CurrentStatus);
        Assert.Equal("looks good", result.Data.StatusHistory.Single().Notes);
        var notification = fx.Db.Notifications.Single();
        Assert.Equal(candidateUserId, notification.UserId);
        Assert.Equal("Application Viewed", notification.Title);
    }

    [Fact]
    public async Task UpdateStatus_SameStatus_WritesNoHistoryOrNotification()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, _, _) = await SeedApplicationAsync(fx, companyId, status: ApplicationStatus.Viewed);
        Detach(fx);

        var result = await Applicants(fx).UpdateStatusAsync(owner, appId, new UpdateStatusRequest
        {
            Status = ApplicationStatus.Viewed
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(fx.Db.ApplicationStatusHistories);
        Assert.Empty(fx.Db.Notifications);
    }

    // 2.3.4 create interview -------------------------------------------------

    [Fact]
    public async Task CreateInterview_Future_Succeeds_NotifiesAndLists()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, candidateUserId, _) = await SeedApplicationAsync(fx, companyId);
        Detach(fx);

        var created = await Interviews(fx).CreateAsync(owner, appId, new CreateInterviewRequest
        {
            InterviewDate = DateTime.UtcNow.AddDays(2),
            InterviewType = InterviewType.Online,
            LocationOrLink = "https://meet.example/x",
            Notes = "round 1"
        });

        Assert.True(created.Success, string.Join("; ", created.Errors));
        Assert.Equal(InterviewStatus.Scheduled, created.Data!.Status);

        Detach(fx);
        var list = await Interviews(fx).ListAsync(owner, appId);
        Assert.True(list.Success);
        Assert.Single(list.Data!);

        var notification = fx.Db.Notifications.Single();
        Assert.Equal(candidateUserId, notification.UserId);
        Assert.Equal("Interview scheduled", notification.Title);
    }

    [Fact]
    public async Task CreateInterview_PastDate_Validation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, _, _) = await SeedApplicationAsync(fx, companyId);
        Detach(fx);

        var result = await Interviews(fx).CreateAsync(owner, appId, new CreateInterviewRequest
        {
            InterviewDate = DateTime.UtcNow.AddDays(-1),
            InterviewType = InterviewType.Online
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task CreateInterview_MissingType_Validation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, _, _) = await SeedApplicationAsync(fx, companyId);
        Detach(fx);

        var result = await Interviews(fx).CreateAsync(owner, appId, new CreateInterviewRequest
        {
            InterviewDate = DateTime.UtcNow.AddDays(1)
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task CreateInterview_FromShortlisted_AutoAdvancesToInterviewing()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, _, _) = await SeedApplicationAsync(fx, companyId, status: ApplicationStatus.Shortlisted);
        Detach(fx);

        var result = await Interviews(fx).CreateAsync(owner, appId, new CreateInterviewRequest
        {
            InterviewDate = DateTime.UtcNow.AddDays(1),
            InterviewType = InterviewType.Onsite
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var app = await fx.Db.Applications.FindAsync(appId);
        Assert.Equal(ApplicationStatus.Interviewing, app!.CurrentStatus);
        Assert.Contains(
            fx.Db.ApplicationStatusHistories,
            h => h.ApplicationId == appId && h.Status == ApplicationStatus.Interviewing);
    }

    [Fact]
    public async Task CreateInterview_TerminalStatus_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, _, _) = await SeedApplicationAsync(fx, companyId, status: ApplicationStatus.Rejected);
        Detach(fx);

        var result = await Interviews(fx).CreateAsync(owner, appId, new CreateInterviewRequest
        {
            InterviewDate = DateTime.UtcNow.AddDays(1),
            InterviewType = InterviewType.Phone
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    // 2.3.5 notify applicant -------------------------------------------------

    [Fact]
    public async Task Notify_CreatesNotificationForCandidate()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, candidateUserId, _) = await SeedApplicationAsync(fx, companyId);
        Detach(fx);

        var result = await Notifier(fx).NotifyAsync(owner, appId, new SendNotificationRequest
        {
            Title = "Next steps",
            Content = "We will contact you soon."
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(candidateUserId, result.Data!.UserId);
        Assert.Equal("Next steps", result.Data.Title);
        Assert.False(result.Data.IsRead);
        Assert.Single(fx.Db.Notifications);
    }

    [Fact]
    public async Task Notify_BlankTitle_Validation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, companyId) = await SeedEmployerAsync(fx, "hr@example.com");
        var (appId, _, _) = await SeedApplicationAsync(fx, companyId);
        Detach(fx);

        var result = await Notifier(fx).NotifyAsync(owner, appId, new SendNotificationRequest { Title = "   " });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Empty(fx.Db.Notifications);
    }

    [Fact]
    public async Task Notify_ForeignApplication_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (owner, _) = await SeedEmployerAsync(fx, "hr@example.com");
        var (_, otherCompany) = await SeedEmployerAsync(fx, "other@example.com");
        var (foreignAppId, _, _) = await SeedApplicationAsync(fx, otherCompany);
        Detach(fx);

        var result = await Notifier(fx).NotifyAsync(owner, foreignAppId, new SendNotificationRequest { Title = "Hi" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
