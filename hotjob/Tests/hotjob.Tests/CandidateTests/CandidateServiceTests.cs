using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.CandidateTests;

/// <summary>
/// Candidate-side reads: my applications (status + timeline) and the
/// notifications employers sent. Every query must scope to the caller's own
/// rows — cross-user leaks here are an IDOR.
/// </summary>
public sealed class CandidateServiceTests
{
    private static ICandidateService Service(TestFixture fx) => new CandidateService(fx.Db);

    private static void Detach(TestFixture fx)
    {
        foreach (var entry in fx.Db.ChangeTracker.Entries().ToArray())
            entry.State = EntityState.Detached;
    }

    private static async Task<(string UserId, int CandidateId)> SeedCandidateAsync(TestFixture fx, string email)
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = $"Name {email}" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();
        return (user.Id, candidate.Id);
    }

    private static async Task<int> SeedJobAsync(TestFixture fx, string companyName, string jobTitle)
    {
        var company = new CompanyProfile { CompanyName = companyName, Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = jobTitle,
            Description = "d",
            Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task<int> SeedApplicationAsync(
        TestFixture fx,
        int candidateId,
        int jobId,
        ApplicationStatus status = ApplicationStatus.Pending,
        DateTime? appliedAt = null,
        params string[] historyNotes)
    {
        var resume = new Resume { CandidateId = candidateId, Title = "CV" };
        fx.Db.Resumes.Add(resume);
        await fx.Db.SaveChangesAsync();

        var application = new Application
        {
            JobPostingId = jobId,
            ResumeId = resume.Id,
            CandidateId = candidateId,
            CurrentStatus = status,
            AppliedAt = appliedAt ?? DateTime.UtcNow
        };
        fx.Db.Applications.Add(application);
        await fx.Db.SaveChangesAsync();

        var stamp = DateTime.UtcNow;
        foreach (var note in historyNotes)
        {
            fx.Db.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.Id,
                Status = status,
                Notes = note,
                ChangedAt = stamp
            });
        }
        await fx.Db.SaveChangesAsync();
        return application.Id;
    }

    // GetMyApplications ------------------------------------------------------

    [Fact]
    public async Task GetMyApplications_ReturnsOwnApplicationsWithJobCompanyAndHistory()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, myCandidateId) = await SeedCandidateAsync(fx, "me@example.com");
        var (_, otherCandidateId) = await SeedCandidateAsync(fx, "other@example.com");
        var jobId = await SeedJobAsync(fx, "Acme", "Backend Engineer");
        await SeedApplicationAsync(fx, myCandidateId, jobId, ApplicationStatus.Offered, historyNotes: "advanced");
        await SeedApplicationAsync(fx, otherCandidateId, await SeedJobAsync(fx, "Globex", "Designer"));
        Detach(fx);

        var result = await Service(fx).GetMyApplicationsAsync(me);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var item = Assert.Single(result.Data!);
        Assert.Equal("Backend Engineer", item.JobTitle);
        Assert.Equal("Acme", item.CompanyName);
        Assert.Equal(ApplicationStatus.Offered, item.Status);
        Assert.Equal("advanced", Assert.Single(item.History).Notes);
    }

    [Fact]
    public async Task GetMyApplications_NoCandidateProfile_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("ghost@example.com", "Str0ng!Pass", AppRoles.Candidate);

        var result = await Service(fx).GetMyApplicationsAsync(user.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetMyApplications_OrdersNewestFirst()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, candidateId) = await SeedCandidateAsync(fx, "me@example.com");
        var older = await SeedJobAsync(fx, "Acme", "Old Job");
        var newer = await SeedJobAsync(fx, "BetaCorp", "New Job");
        await SeedApplicationAsync(fx, candidateId, older, appliedAt: DateTime.UtcNow.AddDays(-5));
        await SeedApplicationAsync(fx, candidateId, newer, appliedAt: DateTime.UtcNow);
        Detach(fx);

        var result = await Service(fx).GetMyApplicationsAsync(me);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("New Job", result.Data![0].JobTitle);
        Assert.Equal("Old Job", result.Data[1].JobTitle);
    }

    [Fact]
    public async Task GetMyApplications_Anonymous_Unauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await Service(fx).GetMyApplicationsAsync(null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    // GetMyNotifications -----------------------------------------------------

    [Fact]
    public async Task GetMyNotifications_ReturnsOwnNewestFirst()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, _) = await SeedCandidateAsync(fx, "me@example.com");
        var (other, _) = await SeedCandidateAsync(fx, "other@example.com");
        fx.Db.Notifications.Add(new Notification { UserId = me, Title = "Older", CreatedAt = DateTime.UtcNow.AddDays(-1) });
        fx.Db.Notifications.Add(new Notification { UserId = me, Title = "Newer", CreatedAt = DateTime.UtcNow });
        fx.Db.Notifications.Add(new Notification { UserId = other, Title = "Not mine" });
        await fx.Db.SaveChangesAsync();
        Detach(fx);

        var result = await Service(fx).GetMyNotificationsAsync(me);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("Newer", result.Data[0].Title);
        Assert.Equal("Older", result.Data[1].Title);
        Assert.DoesNotContain(result.Data, n => n.Title == "Not mine");
    }

    [Fact]
    public async Task GetMyNotifications_Anonymous_Unauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await Service(fx).GetMyNotificationsAsync(null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    // MarkNotificationRead ---------------------------------------------------

    [Fact]
    public async Task MarkNotificationRead_OwnRow_SetsReadAndPersists()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, _) = await SeedCandidateAsync(fx, "me@example.com");
        var note = new Notification { UserId = me, Title = "Hello" };
        fx.Db.Notifications.Add(note);
        await fx.Db.SaveChangesAsync();
        var id = note.Id;
        Detach(fx);

        var result = await Service(fx).MarkNotificationReadAsync(me, id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.IsRead);
        Detach(fx);
        Assert.True(await fx.Db.Notifications.Where(n => n.Id == id).Select(n => n.IsRead).SingleAsync());
    }

    [Fact]
    public async Task MarkNotificationRead_OtherUsersRow_NotFoundAndUnchanged()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, _) = await SeedCandidateAsync(fx, "me@example.com");
        var (other, _) = await SeedCandidateAsync(fx, "other@example.com");
        var note = new Notification { UserId = other, Title = "Not mine" };
        fx.Db.Notifications.Add(note);
        await fx.Db.SaveChangesAsync();
        var id = note.Id;
        Detach(fx);

        var result = await Service(fx).MarkNotificationReadAsync(me, id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
        Detach(fx);
        Assert.False(await fx.Db.Notifications.Where(n => n.Id == id).Select(n => n.IsRead).SingleAsync());
    }
}
