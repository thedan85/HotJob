using hotjob.Data;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Billing;
using hotjob.Services.ML;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.MLTests;

/// <summary>Dev-only synthetic seed data must be balanced, idempotent and train the real FastTree model.</summary>
public sealed class SyntheticMatchTrainingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeedAndTrain_WithThreePublishedAndOneDraftJob_SeedsBalancedData_AndPromotesModel()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var admin = await fx.SeedUserAsync("synthetic-admin@example.com", "Str0ng!Pass", AppRoles.Admin);

        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var react = await MatchTestData.GetOrCreateSkillAsync(fx, "React");
        var azure = await MatchTestData.GetOrCreateSkillAsync(fx, "Azure");

        var jobs = new[]
        {
            await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 10), (sql.Id, 5)], 3, title: "Synthetic .NET"),
            await MatchTestData.CreateJobAsync(fx, company.Id, [(react.Id, 10)], 2, title: "Synthetic Frontend"),
            await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 5), (sql.Id, 5), (react.Id, 5)], 3, title: "Synthetic Fullstack"),
            await MatchTestData.CreateJobAsync(fx, company.Id, [(sql.Id, 10), (azure.Id, 5)], 1, status: JobStatus.Draft, title: "Synthetic Data")
        };

        var matchService = new MatchScoreService(
            fx.Db,
            temp.CreateStore(),
            new CandidatePremiumService(fx.Db, clock),
            Options.Create(temp.CreateOptions()),
            clock,
            NullLogger<MatchScoreService>.Instance);
        var service = new SyntheticMatchTrainingService(
            fx.Db, fx.UserManager, matchService, clock, NullLogger<SyntheticMatchTrainingService>.Instance);

        var result = await service.SeedAndTrainAsync(admin.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(14, result.Data!.CandidateCount);
        Assert.Equal(14, result.Data.CandidateCreated);
        Assert.Equal(56, result.Data.ApplicationCount);
        Assert.Equal(56, result.Data.ApplicationCreated);
        Assert.Equal(24, result.Data.OfferedCount);
        Assert.Equal(16, result.Data.RejectedCount);
        Assert.Equal(16, result.Data.WithdrawnCount);
        Assert.True(result.Data.Training.Trained);
        Assert.True(result.Data.Training.Promoted);
        Assert.Equal(56, result.Data.Training.RecordCount);
        Assert.True(File.Exists(temp.ModelPath));

        var markerUsers = await fx.Db.Users
            .Where(user => user.Email!.StartsWith("ml-demo-candidate-"))
            .ToListAsync();
        Assert.Equal(14, markerUsers.Count);

        var markerCandidateIds = await fx.Db.Candidates
            .Where(candidate => markerUsers.Select(user => user.Id).Contains(candidate.UserId))
            .Select(candidate => candidate.Id)
            .ToListAsync();
        var applications = await fx.Db.Applications
            .Where(application => markerCandidateIds.Contains(application.CandidateId))
            .ToListAsync();
        Assert.Equal(56, applications.Count);
        Assert.Equal(24, applications.Count(application => application.CurrentStatus == ApplicationStatus.Offered));
        Assert.Equal(16, applications.Count(application => application.CurrentStatus == ApplicationStatus.Rejected));
        Assert.Equal(16, applications.Count(application => application.CurrentStatus == ApplicationStatus.Withdrawn));

        var rerun = await service.SeedAndTrainAsync(admin.Id);

        Assert.True(rerun.Success, string.Join("; ", rerun.Errors));
        Assert.Equal(14, rerun.Data!.CandidateCount);
        Assert.Equal(0, rerun.Data.CandidateCreated);
        Assert.Equal(56, rerun.Data.ApplicationCount);
        Assert.Equal(0, rerun.Data.ApplicationCreated);
        Assert.Equal(14, await fx.Db.Users.CountAsync(user => user.Email!.StartsWith("ml-demo-candidate-")));
        Assert.Equal(56, await fx.Db.Applications.CountAsync(
            application => markerCandidateIds.Contains(application.CandidateId)));

        var probeCandidate = await fx.Db.Candidates
            .Where(candidate => markerCandidateIds.Contains(candidate.Id))
            .OrderBy(candidate => candidate.Id)
            .FirstAsync();
        var probeResume = await fx.Db.Resumes.FirstAsync(resume => resume.CandidateId == probeCandidate.Id);
        var score = await matchService.GetAsync(probeCandidate.UserId, jobs[0].Id, probeResume.Id);

        Assert.True(score.Success, string.Join("; ", score.Errors));
        Assert.Equal(MatchScoreService.ModelAlgorithm, score.Data!.AlgorithmUsed);
    }

    [Fact]
    public async Task SeedAndTrain_WithFewerThanFourUsableJobs_ReportsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 10)], title: "Only job");

        var matchService = new MatchScoreService(
            fx.Db,
            temp.CreateStore(),
            new CandidatePremiumService(fx.Db, clock),
            Options.Create(temp.CreateOptions()),
            clock,
            NullLogger<MatchScoreService>.Instance);
        var service = new SyntheticMatchTrainingService(
            fx.Db, fx.UserManager, matchService, clock, NullLogger<SyntheticMatchTrainingService>.Instance);

        var result = await service.SeedAndTrainAsync(null);

        Assert.False(result.Success);
        Assert.Contains("At least 4 jobs with skills", string.Join("; ", result.Errors));
        Assert.Equal(0, await fx.Db.Users.CountAsync(user => user.Email!.StartsWith("ml-demo-candidate-")));
    }
}
