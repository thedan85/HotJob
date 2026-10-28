using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.JobAlerts;
using hotjob.Services.ML;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.JobAlertTests;

/// <summary>Job alert generation and read-side isolation (contract D11).</summary>
public sealed class JobAlertServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private static JobAlertService CreateService(
        TestFixture fx,
        TempModelDirectory temp,
        FakeTimeProvider clock,
        IMatchScoreService? scores = null,
        int minMatchPercent = 60)
    {
        var premium = new CandidatePremiumService(fx.Db, clock);
        scores ??= new MatchScoreService(
            fx.Db,
            temp.CreateStore(),
            premium,
            Options.Create(temp.CreateOptions()),
            clock,
            NullLogger<MatchScoreService>.Instance);

        return new JobAlertService(
            fx.Db,
            scores,
            Options.Create(new JobAlertsOptions { MinMatchPercent = minMatchPercent }),
            clock,
            NullLogger<JobAlertService>.Instance);
    }

    private static async Task MakePremiumAsync(TestFixture fx, Candidate candidate, DateTime until)
    {
        var row = await fx.Db.Candidates.SingleAsync(row => row.Id == candidate.Id);
        row.PremiumUntil = until;
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task PublishedJob_NotifiesEveryCandidateAtOrAboveThreshold()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);

        var high = await MatchTestData.CreateCandidateAsync(fx, "high@example.com", ("C#", 3), ("SQL", 3));
        var low = await MatchTestData.CreateCandidateAsync(fx, "low@example.com", ("C#", 3));
        var free = await MatchTestData.CreateCandidateAsync(fx, "free@example.com", ("C#", 3), ("SQL", 3));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var job = await MatchTestData.CreateJobAsync(
            fx, company.Id, [(csharp.Id, 10), (sql.Id, 10)], title: "Backend engineer");

        var created = await CreateService(fx, temp, clock).NotifyForPublishedJobAsync(job.Id);

        Assert.Equal(2, created);
        var alerts = await fx.Db.Notifications.ToListAsync();
        Assert.Equal(2, alerts.Count);
        Assert.All(alerts, alert =>
        {
            Assert.Equal(NotificationKinds.JobAlert, alert.Kind);
            Assert.Equal(job.Id, alert.RelatedJobPostingId);
            Assert.Contains("Backend engineer", alert.Title);
            Assert.Contains(company.CompanyName, alert.Content);
            Assert.Contains("100%", alert.Content);
            Assert.False(alert.IsRead);
            Assert.Equal(clock.GetUtcNow().UtcDateTime, alert.CreatedAt);
        });
        Assert.Contains(alerts, alert => alert.UserId == high.Candidate.UserId);
        Assert.Contains(alerts, alert => alert.UserId == free.Candidate.UserId);
        Assert.DoesNotContain(alerts, alert => alert.UserId == low.Candidate.UserId);
    }

    [Fact]
    public async Task NonPublishedOrExpiredJob_DoesNotNotify()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var candidate = await MatchTestData.CreateCandidateAsync(fx, "candidate@example.com", ("C#", 3));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        await MakePremiumAsync(fx, candidate.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        var draft = await MatchTestData.CreateJobAsync(
            fx, company.Id, [(csharp.Id, 10)], status: JobStatus.Draft);
        var expired = await MatchTestData.CreateJobAsync(
            fx, company.Id, [(csharp.Id, 10)], status: JobStatus.Published);
        expired.ExpiryDate = clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
        await fx.Db.SaveChangesAsync();

        var service = CreateService(fx, temp, clock);

        Assert.Equal(0, await service.NotifyForPublishedJobAsync(draft.Id));
        Assert.Equal(0, await service.NotifyForPublishedJobAsync(expired.Id));
        Assert.Equal(0, await fx.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task BestOfMultipleResumes_IsUsedForTheAlert()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var candidate = await MatchTestData.CreateCandidateAsync(fx, "multi@example.com", ("C#", 3));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var job = await MatchTestData.CreateJobAsync(
            fx, company.Id, [(csharp.Id, 10), (sql.Id, 10)]);
        await MakePremiumAsync(fx, candidate.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        var stronger = new hotjob.Models.Resume.Resume
        {
            CandidateId = candidate.Candidate.Id,
            Title = "Full stack CV",
            ResumeSkills =
            [
                new hotjob.Models.Resume.ResumeSkill { SkillId = csharp.Id, YearsOfExperience = 3 },
                new hotjob.Models.Resume.ResumeSkill { SkillId = sql.Id, YearsOfExperience = 3 }
            ]
        };
        fx.Db.Resumes.Add(stronger);
        await fx.Db.SaveChangesAsync();

        var created = await CreateService(fx, temp, clock).NotifyForPublishedJobAsync(job.Id);

        Assert.Equal(1, created);
        var alert = await fx.Db.Notifications.SingleAsync();
        Assert.Contains("100%", alert.Content);
        var strongerScore = await fx.Db.MatchScores
            .SingleAsync(score => score.ResumeId == stronger.Id && score.JobPostingId == job.Id);
        Assert.Equal(100, strongerScore.MatchPercentage, 6);
    }

    [Fact]
    public async Task PublishingTheSameJobAgain_DoesNotDuplicateAlerts()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var candidate = await MatchTestData.CreateCandidateAsync(fx, "repeat@example.com", ("C#", 3));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 10)]);
        await MakePremiumAsync(fx, candidate.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        var service = CreateService(fx, temp, clock);

        Assert.Equal(1, await service.NotifyForPublishedJobAsync(job.Id));
        Assert.Equal(0, await service.NotifyForPublishedJobAsync(job.Id));
        Assert.Equal(1, await fx.Db.Notifications.CountAsync(
            note => note.Kind == NotificationKinds.JobAlert && note.RelatedJobPostingId == job.Id));
    }

    [Fact]
    public async Task InactivePremiumCandidate_IsExcluded()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var candidate = await MatchTestData.CreateCandidateAsync(fx, "inactive@example.com", ("C#", 3));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 10)]);
        await MakePremiumAsync(fx, candidate.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        var user = await fx.Db.Users.SingleAsync(user => user.Id == candidate.Candidate.UserId);
        user.IsActive = false;
        await fx.Db.SaveChangesAsync();

        var created = await CreateService(fx, temp, clock).NotifyForPublishedJobAsync(job.Id);

        Assert.Equal(0, created);
        Assert.Equal(0, await fx.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task JobWithoutSkills_DoesNotNotify()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var candidate = await MatchTestData.CreateCandidateAsync(fx, "no-skills@example.com", ("C#", 3));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, []);
        await MakePremiumAsync(fx, candidate.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        var created = await CreateService(fx, temp, clock).NotifyForPublishedJobAsync(job.Id);

        Assert.Equal(0, created);
        Assert.Equal(0, await fx.Db.Notifications.CountAsync());
    }

    [Fact]
    public async Task GetAlerts_ReturnsOnlyCallersJobAlertNotifications_NewestFirst()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var me = await MatchTestData.CreateCandidateAsync(fx, "me@example.com");
        var other = await MatchTestData.CreateCandidateAsync(fx, "other@example.com");
        await MakePremiumAsync(fx, me.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));
        await MakePremiumAsync(fx, other.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        fx.Db.Notifications.AddRange(
            new Notification
            {
                UserId = me.Candidate.UserId,
                Title = "Older alert",
                Kind = NotificationKinds.JobAlert,
                CreatedAt = clock.GetUtcNow().UtcDateTime.AddHours(-2)
            },
            new Notification
            {
                UserId = me.Candidate.UserId,
                Title = "Newer alert",
                Kind = NotificationKinds.JobAlert,
                CreatedAt = clock.GetUtcNow().UtcDateTime.AddHours(-1)
            },
            new Notification
            {
                UserId = me.Candidate.UserId,
                Title = "Employer message",
                CreatedAt = clock.GetUtcNow().UtcDateTime
            },
            new Notification
            {
                UserId = other.Candidate.UserId,
                Title = "Other candidate alert",
                Kind = NotificationKinds.JobAlert
            });
        await fx.Db.SaveChangesAsync();

        var result = await CreateService(fx, temp, clock).GetAlertsAsync(me.Candidate.UserId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var feed = result.Data!;
        Assert.Equal(2, feed.Alerts.Count);
        Assert.Equal("Newer alert", feed.Alerts[0].Title);
        Assert.Equal("Older alert", feed.Alerts[1].Title);
        Assert.Equal(2, feed.UnreadCount);
        Assert.DoesNotContain(feed.Alerts, alert => alert.Title == "Employer message");
        Assert.DoesNotContain(feed.Alerts, alert => alert.Title == "Other candidate alert");
    }

    [Fact]
    public async Task MarkAllRead_AffectsOnlyTheCallersJobAlerts()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var me = await MatchTestData.CreateCandidateAsync(fx, "me@example.com");
        var other = await MatchTestData.CreateCandidateAsync(fx, "other@example.com");
        await MakePremiumAsync(fx, me.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));
        await MakePremiumAsync(fx, other.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        var myAlert = new Notification
        {
            UserId = me.Candidate.UserId,
            Title = "My alert",
            Kind = NotificationKinds.JobAlert
        };
        var myMessage = new Notification { UserId = me.Candidate.UserId, Title = "My message" };
        var otherAlert = new Notification
        {
            UserId = other.Candidate.UserId,
            Title = "Other alert",
            Kind = NotificationKinds.JobAlert
        };
        fx.Db.Notifications.AddRange(myAlert, myMessage, otherAlert);
        await fx.Db.SaveChangesAsync();

        var service = CreateService(fx, temp, clock);
        var result = await service.MarkAllReadAsync(me.Candidate.UserId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(1, result.Data);
        fx.Db.ChangeTracker.Clear();
        Assert.True(await fx.Db.Notifications.Where(note => note.Id == myAlert.Id).Select(note => note.IsRead).SingleAsync());
        Assert.False(await fx.Db.Notifications.Where(note => note.Id == myMessage.Id).Select(note => note.IsRead).SingleAsync());
        Assert.False(await fx.Db.Notifications.Where(note => note.Id == otherAlert.Id).Select(note => note.IsRead).SingleAsync());
    }

    [Fact]
    public async Task FreeCandidate_SeesExistingAlerts()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var candidate = await MatchTestData.CreateCandidateAsync(fx, "free@example.com");

        fx.Db.Notifications.Add(new Notification
        {
            UserId = candidate.Candidate.UserId,
            Title = "Old alert",
            Kind = NotificationKinds.JobAlert
        });
        await fx.Db.SaveChangesAsync();

        var result = await CreateService(fx, temp, clock).GetAlertsAsync(candidate.Candidate.UserId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var alert = Assert.Single(result.Data!.Alerts);
        Assert.Equal("Old alert", alert.Title);
        Assert.Equal(1, result.Data.UnreadCount);
    }

    [Fact]
    public async Task ScoreExactlyAtThreshold_Notifies()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var candidate = await MatchTestData.CreateCandidateAsync(fx, "boundary@example.com", ("C#", 3));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var job = await MatchTestData.CreateJobAsync(
            fx, company.Id, [(csharp.Id, 10), (sql.Id, 10)]);
        await MakePremiumAsync(fx, candidate.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        var created = await CreateService(fx, temp, clock, minMatchPercent: 50)
            .NotifyForPublishedJobAsync(job.Id);

        Assert.Equal(1, created);
        var alert = await fx.Db.Notifications.SingleAsync();
        Assert.Contains("50%", alert.Content);
    }

    [Fact]
    public async Task JobAlerts_AreNotReturnedInTheEmployerMessagesFeed()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var candidate = await MatchTestData.CreateCandidateAsync(fx, "feed@example.com");
        await MakePremiumAsync(fx, candidate.Candidate, clock.GetUtcNow().UtcDateTime.AddDays(30));

        fx.Db.Notifications.AddRange(
            new Notification
            {
                UserId = candidate.Candidate.UserId,
                Title = "New matching job: Backend engineer",
                Kind = NotificationKinds.JobAlert
            },
            new Notification
            {
                UserId = candidate.Candidate.UserId,
                Title = "Employer message"
            });
        await fx.Db.SaveChangesAsync();

        var result = await new hotjob.Services.Candidate.CandidateService(fx.Db)
            .GetMyNotificationsAsync(candidate.Candidate.UserId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var message = Assert.Single(result.Data!);
        Assert.Equal("Employer message", message.Title);
    }

    [Fact]
    public async Task MarkAllRead_Anonymous_Unauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);

        var result = await CreateService(fx, temp, clock).MarkAllReadAsync(null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }
}
