using hotjob.Data;
using hotjob.DTOs.Candidate;
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

    private static async Task<int[]> SeedApplicationBatchAsync(
        TestFixture fx,
        int candidateId,
        int count,
        Func<int, string>? title = null,
        Func<int, ApplicationStatus>? status = null,
        Func<int, DateTime>? appliedAt = null)
    {
        var company = new CompanyProfile
        {
            CompanyName = $"Batch Co {Guid.NewGuid():N}",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var resume = new Resume { CandidateId = candidateId, Title = "CV" };
        fx.Db.Resumes.Add(resume);
        await fx.Db.SaveChangesAsync();

        var ids = new int[count];
        for (var index = 0; index < count; index++)
        {
            var job = new JobPosting
            {
                CompanyProfileId = company.Id,
                Title = title?.Invoke(index) ?? $"Job {index}",
                Description = "d",
                Status = JobStatus.Published
            };
            fx.Db.JobPostings.Add(job);
            await fx.Db.SaveChangesAsync();

            var application = new Application
            {
                JobPostingId = job.Id,
                ResumeId = resume.Id,
                CandidateId = candidateId,
                CurrentStatus = status?.Invoke(index) ?? ApplicationStatus.Pending,
                AppliedAt = appliedAt?.Invoke(index) ?? DateTime.UtcNow.AddMinutes(index)
            };
            fx.Db.Applications.Add(application);
            await fx.Db.SaveChangesAsync();
            ids[index] = application.Id;
        }

        return ids;
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

    // GetMyApplicationsPage --------------------------------------------------

    [Fact]
    public async Task GetMyApplicationsPage_SlicesRequestedPageAndReportsTotal()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, candidateId) = await SeedCandidateAsync(fx, "paged-apps@example.com");
        await SeedApplicationBatchAsync(fx, candidateId, 5);
        Detach(fx);

        var result = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery { Page = 1, PageSize = 2 });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var page = result.Data!;
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(new[] { "Job 4", "Job 3" }, page.Items.Select(item => item.JobTitle));
        Assert.Equal(5, page.TotalApplications);
    }

    [Fact]
    public async Task GetMyApplicationsPage_OrderIsStableAcrossPages()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, candidateId) = await SeedCandidateAsync(fx, "stable-pages@example.com");
        var ids = await SeedApplicationBatchAsync(
            fx, candidateId, 6,
            appliedAt: _ => new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        Detach(fx);

        var first = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery { Page = 1, PageSize = 2 });
        var second = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery { Page = 2, PageSize = 2 });
        var third = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery { Page = 3, PageSize = 2 });

        var seen = first.Data!.Items
            .Concat(second.Data!.Items)
            .Concat(third.Data!.Items)
            .Select(item => item.ApplicationId)
            .ToArray();

        Assert.Equal(6, seen.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(id => id), seen);
    }

    [Fact]
    public async Task GetMyApplicationsPage_BeyondLastPage_ReturnsEmptyPageAndKeepsTotal()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, candidateId) = await SeedCandidateAsync(fx, "beyond@example.com");
        await SeedApplicationBatchAsync(fx, candidateId, 3);
        Detach(fx);

        var result = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery { Page = 99, PageSize = 2 });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var page = result.Data!;
        Assert.Empty(page.Items);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(99, page.Page);
    }

    [Fact]
    public async Task GetMyApplicationsPage_ClampsPageSizeToHardMaximum()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, candidateId) = await SeedCandidateAsync(fx, "cap@example.com");
        await SeedApplicationBatchAsync(fx, candidateId, 3);
        Detach(fx);

        var result = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery { Page = 1, PageSize = 1000 });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(MyApplicationsQuery.MaxPageSize, result.Data!.PageSize);
        Assert.Equal(50, result.Data.PageSize);
    }

    [Fact]
    public async Task GetMyApplicationsPage_UsesDefaultPageSizeWhenNotSet()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, candidateId) = await SeedCandidateAsync(fx, "default-size@example.com");
        await SeedApplicationBatchAsync(fx, candidateId, 3);
        Detach(fx);

        var result = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery { Page = 0, PageSize = 0 });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(MyApplicationsQuery.DefaultPageSize, result.Data!.PageSize);
        Assert.Equal(10, result.Data.PageSize);
        Assert.Equal(1, result.Data.Page);
    }

    [Fact]
    public async Task GetMyApplicationsPage_ReturnsOnlyCallersApplications()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, myCandidateId) = await SeedCandidateAsync(fx, "mine@example.com");
        var (_, otherCandidateId) = await SeedCandidateAsync(fx, "theirs@example.com");
        await SeedApplicationBatchAsync(fx, myCandidateId, 2, title: index => $"Mine {index}");
        await SeedApplicationBatchAsync(fx, otherCandidateId, 3, title: index => $"Theirs {index}");
        Detach(fx);

        var result = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery { Page = 1, PageSize = 10 });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var page = result.Data!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalApplications);
        Assert.All(page.Items, item => Assert.StartsWith("Mine ", item.JobTitle));
    }

    [Fact]
    public async Task GetMyApplicationsPage_FilterAndPagingApplyTogether()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, candidateId) = await SeedCandidateAsync(fx, "filter-pages@example.com");
        await SeedApplicationBatchAsync(
            fx, candidateId, 5,
            title: index => index < 3 ? "Backend Engineer" : "Designer",
            status: index => index switch
            {
                0 => ApplicationStatus.Pending,
                1 => ApplicationStatus.Shortlisted,
                2 => ApplicationStatus.Offered,
                3 => ApplicationStatus.Rejected,
                _ => ApplicationStatus.Pending
            });
        Detach(fx);

        var first = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery
            {
                Search = "backend",
                StatusGroup = "active",
                Page = 1,
                PageSize = 1
            });
        var second = await Service(fx).GetMyApplicationsPageAsync(
            me, new MyApplicationsQuery
            {
                Search = "backend",
                StatusGroup = "active",
                Page = 2,
                PageSize = 1
            });

        Assert.True(first.Success, string.Join("; ", first.Errors));
        var page = first.Data!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(5, page.TotalApplications);
        Assert.Equal(3, page.InProgressCount);
        Assert.Equal(1, page.OfferCount);
        Assert.Single(page.Items);
        Assert.Single(second.Data!.Items);
        Assert.NotEqual(page.Items[0].ApplicationId, second.Data.Items[0].ApplicationId);
        Assert.All(page.Items.Concat(second.Data.Items),
            item => Assert.Equal("Backend Engineer", item.JobTitle));
    }

    [Fact]
    public async Task GetMyApplicationsPage_Anonymous_Unauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await Service(fx).GetMyApplicationsPageAsync(
            null, new MyApplicationsQuery());

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task GetMyApplicationsPage_NoCandidateProfile_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("ghost-page@example.com", "Str0ng!Pass", AppRoles.Candidate);

        var result = await Service(fx).GetMyApplicationsPageAsync(
            user.Id, new MyApplicationsQuery());

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
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

        var result = await Service(fx).GetMyNotificationsAsync(me, 1, 20);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal("Newer", result.Data.Items[0].Title);
        Assert.Equal("Older", result.Data.Items[1].Title);
        Assert.DoesNotContain(result.Data.Items, n => n.Title == "Not mine");
    }

    [Fact]
    public async Task GetMyNotifications_PagesWithoutOverlap()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (me, _) = await SeedCandidateAsync(fx, "paged@example.com");
        for (var index = 0; index < 3; index++)
        {
            fx.Db.Notifications.Add(new Notification
            {
                UserId = me,
                Title = $"Note {index}",
                CreatedAt = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc).AddMinutes(index)
            });
        }
        await fx.Db.SaveChangesAsync();
        Detach(fx);

        var first = await Service(fx).GetMyNotificationsAsync(me, 1, 2);
        var second = await Service(fx).GetMyNotificationsAsync(me, 2, 2);

        Assert.True(first.Success, string.Join("; ", first.Errors));
        Assert.Equal(3, first.Data!.TotalCount);
        Assert.Equal(2, first.Data.Items.Count);
        Assert.Equal(2, first.Data.TotalPages);
        Assert.Single(second.Data!.Items);
        Assert.DoesNotContain(second.Data.Items, note => first.Data.Items.Any(other => other.Id == note.Id));
    }

    [Fact]
    public async Task GetMyNotifications_Anonymous_Unauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await Service(fx).GetMyNotificationsAsync(null, 1, 20);

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
