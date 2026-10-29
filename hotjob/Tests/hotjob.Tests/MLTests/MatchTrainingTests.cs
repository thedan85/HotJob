using hotjob.Data;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Services.Billing;
using hotjob.Services.ML;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.MLTests;

/// <summary>Training guards, promotion rules and the trained model's scoring path (contract D9).</summary>
public sealed class MatchTrainingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private static MatchScoreService CreateService(
        TestFixture fx,
        TempModelDirectory temp,
        FakeTimeProvider clock,
        MatchModelStore? store = null,
        int minTrainingRecords = 50) =>
        new(
            fx.Db,
            store ?? temp.CreateStore(),
            new CandidatePremiumService(fx.Db, clock),
            Options.Create(temp.CreateOptions(minTrainingRecords)),
            clock,
            NullLogger<MatchScoreService>.Instance);

    private static async Task SeedApplicationsAsync(
        TestFixture fx,
        int companyId,
        hotjob.Models.Users.Candidate candidate,
        hotjob.Models.Resume.Resume resume,
        int skillId,
        ApplicationStatus status,
        int count,
        string? latestNote = null)
    {
        for (var index = 0; index < count; index++)
        {
            var job = await MatchTestData.CreateJobAsync(
                fx,
                companyId,
                [(skillId, 10)],
                experienceRequired: 1,
                title: $"Training role {status} {index}");
            await MatchTestData.CreateApplicationAsync(fx, job, resume, candidate, status, latestNote);
        }
    }

    [Fact]
    public async Task Train_BelowMinimumRecords_ReportsInsufficientData_AndKeepsWeightedRule()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "guard-count@example.com", ("C#", 5));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var java = await MatchTestData.GetOrCreateSkillAsync(fx, "Java");
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, csharp.Id, ApplicationStatus.Offered, 5);
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, java.Id, ApplicationStatus.Rejected, 5);

        var service = CreateService(fx, temp, clock);
        var result = await service.TrainAsync(null);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(result.Data!.Trained);
        Assert.False(result.Data.Promoted);
        Assert.Equal(10, result.Data.RecordCount);
        Assert.Null(result.Data.Accuracy);
        Assert.Null(result.Data.ModelPath);
        Assert.Contains("Insufficient data", result.Data.Message);
        Assert.False(File.Exists(temp.ModelPath));
        Assert.Equal(0, await fx.Db.ModelTrainingLogs.CountAsync());

        var probe = await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 10)], experienceRequired: 1);
        var score = await service.GetAsync(candidate.UserId, probe.Id, resume.Id);
        Assert.Equal(MatchScoreService.WeightedRuleAlgorithm, score.Data!.AlgorithmUsed);
    }

    [Fact]
    public async Task Train_WhenAClassHasFewerThanFiveRows_ReportsInsufficientData()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "guard-class@example.com", ("C#", 5));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var java = await MatchTestData.GetOrCreateSkillAsync(fx, "Java");
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, csharp.Id, ApplicationStatus.Offered, 4);
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, java.Id, ApplicationStatus.Rejected, 46);

        var service = CreateService(fx, temp, clock);
        var result = await service.TrainAsync(null);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(result.Data!.Trained);
        Assert.Equal(50, result.Data.RecordCount);
        Assert.Contains("Insufficient data", result.Data.Message);
        Assert.False(File.Exists(temp.ModelPath));
        Assert.Equal(0, await fx.Db.ModelTrainingLogs.CountAsync());
    }

    [Fact]
    public async Task Train_WithEnoughBalancedData_PromotesModel_AndScoresWithFastTree()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "train-success@example.com", ("C#", 5));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var trainer = await fx.SeedUserAsync("trainer@example.com", "Str0ng!Pass", AppRoles.Admin);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var java = await MatchTestData.GetOrCreateSkillAsync(fx, "Java");
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, csharp.Id, ApplicationStatus.Offered, 25);
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, java.Id, ApplicationStatus.Rejected, 20);
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, java.Id, ApplicationStatus.Withdrawn, 5);

        // Auto-rejections from closing a job are not hiring decisions and must not train.
        await SeedApplicationsAsync(
            fx, company.Id, candidate, resume, java.Id, ApplicationStatus.Rejected, 10, latestNote: "Job closed");

        var probe = await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 10)], experienceRequired: 1);
        var service = CreateService(fx, temp, clock);

        var before = await service.GetAsync(candidate.UserId, probe.Id, resume.Id);
        Assert.Equal(MatchScoreService.WeightedRuleAlgorithm, before.Data!.AlgorithmUsed);

        var result = await service.TrainAsync(trainer.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.Trained);
        Assert.True(result.Data.Promoted);
        Assert.Equal(50, result.Data.RecordCount);
        Assert.InRange(result.Data.Accuracy!.Value, 0, 1);
        Assert.NotNull(result.Data.ModelPath);
        Assert.True(File.Exists(result.Data.ModelPath));

        var log = await fx.Db.ModelTrainingLogs.SingleAsync();
        Assert.Equal(50, log.RecordCount);
        Assert.Equal(result.Data.Accuracy.Value, log.AccuracyScore, 10);
        Assert.Equal(result.Data.ModelPath, log.ModelFilePath);
        Assert.Equal(trainer.Id, log.TrainedByUserId);
        Assert.True(log.IsActive);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, log.TrainedAt);

        var after = await service.GetAsync(candidate.UserId, probe.Id, resume.Id);
        Assert.Equal(MatchScoreService.ModelAlgorithm, after.Data!.AlgorithmUsed);
        Assert.InRange(after.Data.MatchPercentage, 0, 100);

        // A model promoted after a score was calculated makes that score stale.
        clock.Advance(TimeSpan.FromHours(1));
        var active = await fx.Db.ModelTrainingLogs.SingleAsync(entry => entry.IsActive);
        active.TrainedAt = clock.GetUtcNow().UtcDateTime;
        await fx.Db.SaveChangesAsync();

        var refreshed = await service.GetAsync(candidate.UserId, probe.Id, resume.Id);

        Assert.Equal(clock.GetUtcNow().UtcDateTime, refreshed.Data!.CalculatedAt);
        Assert.Equal(MatchScoreService.ModelAlgorithm, refreshed.Data.AlgorithmUsed);
        Assert.Equal(1, await fx.Db.MatchScores.CountAsync(score => score.JobPostingId == probe.Id));
    }

    [Fact]
    public async Task Train_WithLowerAccuracy_KeepsTheActiveModel_ButStillLogsTheRun()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "promotion@example.com", ("C#", 5));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var java = await MatchTestData.GetOrCreateSkillAsync(fx, "Java");
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, csharp.Id, ApplicationStatus.Offered, 25);
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, java.Id, ApplicationStatus.Rejected, 25);
        var service = CreateService(fx, temp, clock);

        var first = await service.TrainAsync(null);
        Assert.True(first.Data!.Promoted);
        Assert.True(File.Exists(temp.ModelPath));
        var bytesBefore = await File.ReadAllBytesAsync(temp.ModelPath);

        // Pretend the active model scored perfectly; any realistic retrain cannot beat it.
        var active = await fx.Db.ModelTrainingLogs.SingleAsync(log => log.IsActive);
        active.AccuracyScore = 1.0;
        await fx.Db.SaveChangesAsync();

        // Identical features with opposite labels force the retrain below 100% hold-out accuracy.
        await fx.Db.ApplicationStatusHistories.ExecuteDeleteAsync();
        await fx.Db.Applications.ExecuteDeleteAsync();
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, csharp.Id, ApplicationStatus.Offered, 25);
        await SeedApplicationsAsync(fx, company.Id, candidate, resume, csharp.Id, ApplicationStatus.Rejected, 25);

        var second = await service.TrainAsync(null);

        Assert.True(second.Success, string.Join("; ", second.Errors));
        Assert.True(second.Data!.Trained);
        Assert.False(second.Data.Promoted);
        Assert.Equal(50, second.Data.RecordCount);
        Assert.Null(second.Data.ModelPath);
        Assert.Contains("previous model was kept", second.Data.Message);

        var logs = await fx.Db.ModelTrainingLogs.OrderBy(log => log.Id).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.True(logs[0].IsActive);
        Assert.Equal(1.0, logs[0].AccuracyScore);
        Assert.False(logs[1].IsActive);
        Assert.Null(logs[1].ModelFilePath);
        Assert.Equal(1, logs.Count(log => log.IsActive));
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(temp.ModelPath));
    }
}
