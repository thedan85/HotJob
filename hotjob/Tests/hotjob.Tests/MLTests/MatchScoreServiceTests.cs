using hotjob.DTOs.Candidate;
using hotjob.DTOs.Job;
using hotjob.Models.Application;
using hotjob.Models.ML;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Services.Job;
using hotjob.Services.ML;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.MLTests;

/// <summary>Cold-start scoring, staleness and safe fallbacks (contract D9).</summary>
public sealed class MatchScoreServiceTests
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

    private static ResumeService CreateResumeService(TestFixture fx, FakeTimeProvider clock) =>
        new(
            fx.Db,
            new CvFileStorage(
                Path.Combine(Path.GetTempPath(), "hotjob-cv-tests", Guid.NewGuid().ToString("N")),
                NullLogger<CvFileStorage>.Instance),
            clock,
            NullLogger<ResumeService>.Instance);

    [Fact]
    public async Task ColdStart_UsesWeightedRule_AndPersistsOneScore()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "cold@example.com", ("C#", 2));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var job = await MatchTestData.CreateJobAsync(
            fx,
            company.Id,
            [(csharp.Id, 9), (sql.Id, 6)],
            experienceRequired: 3);

        var result = await CreateService(fx, temp, clock).GetAsync(candidate.UserId, job.Id, resume.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.Available);
        Assert.Equal(60, result.Data.MatchPercentage, 6);
        Assert.Equal(MatchScoreService.WeightedRuleAlgorithm, result.Data.AlgorithmUsed);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, result.Data.CalculatedAt);

        var stored = await fx.Db.MatchScores.SingleAsync();
        Assert.Equal(job.Id, stored.JobPostingId);
        Assert.Equal(resume.Id, stored.ResumeId);
        Assert.Equal(60, stored.MatchPercentage, 6);
    }

    [Fact]
    public async Task TwoResumes_GetDifferentScoresForTheSameJob()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "two-cv@example.com", ("C#", 2));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var job = await MatchTestData.CreateJobAsync(
            fx,
            company.Id,
            [(csharp.Id, 9), (sql.Id, 6)],
            experienceRequired: 3);

        var secondResume = new hotjob.Models.Resume.Resume
        {
            CandidateId = candidate.Id,
            Title = "Full stack CV",
            ResumeSkills =
            [
                new hotjob.Models.Resume.ResumeSkill { SkillId = csharp.Id, YearsOfExperience = 4 },
                new hotjob.Models.Resume.ResumeSkill { SkillId = sql.Id, YearsOfExperience = 4 }
            ]
        };
        fx.Db.Resumes.Add(secondResume);
        await fx.Db.SaveChangesAsync();

        var service = CreateService(fx, temp, clock);
        var first = await service.GetAsync(candidate.UserId, job.Id, resume.Id);
        var second = await service.GetAsync(candidate.UserId, job.Id, secondResume.Id);

        Assert.Equal(60, first.Data!.MatchPercentage, 6);
        Assert.Equal(100, second.Data!.MatchPercentage, 6);
        Assert.Equal(2, await fx.Db.MatchScores.CountAsync());
    }

    [Fact]
    public async Task JobWithoutSkills_HasNoScoreAndNoRow()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "no-skills@example.com", ("C#", 2));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, []);

        var result = await CreateService(fx, temp, clock).GetAsync(candidate.UserId, job.Id, resume.Id);

        Assert.True(result.Success);
        Assert.False(result.Data!.Available);
        Assert.Contains("does not list any required skills", result.Data.Message);
        Assert.Equal(0, await fx.Db.MatchScores.CountAsync());
    }

    [Fact]
    public async Task ResumeSkillChange_InvalidatesAndRecomputesTheScore()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "resume-change@example.com", ("C#", 2));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var job = await MatchTestData.CreateJobAsync(
            fx,
            company.Id,
            [(csharp.Id, 9), (sql.Id, 6)],
            experienceRequired: 3);
        var service = CreateService(fx, temp, clock);

        var before = await service.GetAsync(candidate.UserId, job.Id, resume.Id);
        Assert.Equal(60, before.Data!.MatchPercentage, 6);

        clock.Advance(TimeSpan.FromMinutes(5));
        var update = await CreateResumeService(fx, clock).UpdateAsync(candidate.UserId, resume.Id, new ResumeInput
        {
            Title = "Primary CV",
            Skills =
            [
                new ResumeSkillInput { SkillId = csharp.Id, YearsOfExperience = 2 },
                new ResumeSkillInput { SkillId = sql.Id, YearsOfExperience = 3 }
            ]
        });
        Assert.True(update.Success, string.Join("; ", update.Errors));
        Assert.Equal(0, await fx.Db.MatchScores.CountAsync());

        var after = await service.GetAsync(candidate.UserId, job.Id, resume.Id);

        Assert.Equal(100, after.Data!.MatchPercentage, 6);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, after.Data.CalculatedAt);
        Assert.True(after.Data.CalculatedAt > before.Data.CalculatedAt);
    }

    [Fact]
    public async Task JobSkillChange_InvalidatesAndRecomputesTheScore()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "job-change@example.com", ("C#", 2));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var employer = await MatchTestData.CreateEmployerAsync(fx, company.Id, "job-owner@example.com");
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var job = await MatchTestData.CreateJobAsync(
            fx,
            company.Id,
            [(csharp.Id, 10)],
            experienceRequired: 1);
        var service = CreateService(fx, temp, clock);

        var before = await service.GetAsync(candidate.UserId, job.Id, resume.Id);
        Assert.Equal(100, before.Data!.MatchPercentage, 6);

        clock.Advance(TimeSpan.FromMinutes(5));
        var jobService = new JobService(
            fx.Db,
            new JobPostingRepository(fx.Db),
            new EntitlementService(fx.Db, clock),
            new StubJobAlertService(),
            NullLogger<JobService>.Instance);
        var assign = await jobService.AssignSkillsAsync(employer.UserId, job.Id, new AssignSkillsRequest
        {
            Skills = [new JobSkillInput { SkillId = sql.Id, ImportanceWeight = 10 }]
        });
        Assert.True(assign.Success, string.Join("; ", assign.Errors));
        Assert.Equal(0, await fx.Db.MatchScores.CountAsync());

        var after = await service.GetAsync(candidate.UserId, job.Id, resume.Id);

        Assert.Equal(0, after.Data!.MatchPercentage, 6);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, after.Data.CalculatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptModel_FallsBackToWeightedRuleWithoutThrowing(bool corrupt)
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "bad-model@example.com", ("C#", 2));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 10)], experienceRequired: 1);

        if (corrupt)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temp.ModelPath)!);
            await File.WriteAllTextAsync(temp.ModelPath, "this is not an ML.NET zip model");
        }

        fx.Db.ModelTrainingLogs.Add(new ModelTrainingLog
        {
            RecordCount = 50,
            AccuracyScore = 0.9,
            ModelFilePath = temp.ModelPath,
            TrainedAt = clock.GetUtcNow().UtcDateTime,
            IsActive = true
        });
        await fx.Db.SaveChangesAsync();

        var result = await CreateService(fx, temp, clock).GetAsync(candidate.UserId, job.Id, resume.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(MatchScoreService.WeightedRuleAlgorithm, result.Data!.AlgorithmUsed);
        Assert.Equal(100, result.Data.MatchPercentage, 6);
    }

    [Fact]
    public async Task AdvancedBreakdown_IsGatedForFreeCandidates_AndOpenForPremium()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "premium@example.com", ("C#", 1));
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var sql = await MatchTestData.GetOrCreateSkillAsync(fx, "SQL");
        var job = await MatchTestData.CreateJobAsync(
            fx,
            company.Id,
            [(csharp.Id, 9), (sql.Id, 6)],
            experienceRequired: 3);
        var service = CreateService(fx, temp, clock);

        var basic = await service.GetAsync(candidate.UserId, job.Id, resume.Id);
        Assert.True(basic.Success);

        var free = await service.GetAdvancedAsync(candidate.UserId, job.Id, resume.Id);
        Assert.False(free.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, free.ErrorType);
        Assert.Null(free.Data);
        Assert.Contains("Premium", free.Errors.Single());

        candidate.PremiumUntil = clock.GetUtcNow().AddDays(30).UtcDateTime;
        await fx.Db.SaveChangesAsync();

        var premium = await service.GetAdvancedAsync(candidate.UserId, job.Id, resume.Id);

        Assert.True(premium.Success, string.Join("; ", premium.Errors));
        Assert.Equal(60, premium.Data!.MatchPercentage, 6);
        Assert.Contains(premium.Data.MatchedSkills, skill => skill.SkillName == "C#" && skill.ImportanceWeight == 9);
        Assert.Contains(premium.Data.MissingSkills, skill => skill.SkillName == "SQL" && skill.ImportanceWeight == 6);
        Assert.Equal(-2, premium.Data.ExperienceGap);
        Assert.False(premium.Data.HasAllRequired);
        Assert.NotEmpty(premium.Data.ImprovementHints);

        candidate.PremiumUntil = clock.GetUtcNow().AddMinutes(-1).UtcDateTime;
        await fx.Db.SaveChangesAsync();
        var expired = await service.GetAdvancedAsync(candidate.UserId, job.Id, resume.Id);

        Assert.False(expired.Success);
        Assert.Null(expired.Data);
    }

    [Fact]
    public async Task PremiumGate_IsScopedToTheSignedInCandidate()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var temp = new TempModelDirectory();
        var clock = new FakeTimeProvider(Start);
        var (premiumCandidate, _) = await MatchTestData.CreateCandidateAsync(fx, "paid@example.com", ("C#", 1));
        var (freeCandidate, freeResume) = await MatchTestData.CreateCandidateAsync(fx, "freeloader@example.com", ("C#", 1));
        premiumCandidate.PremiumUntil = clock.GetUtcNow().AddDays(30).UtcDateTime;
        await fx.Db.SaveChangesAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var csharp = await MatchTestData.GetOrCreateSkillAsync(fx, "C#");
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [(csharp.Id, 10)], experienceRequired: 1);
        var service = CreateService(fx, temp, clock);

        var result = await service.GetAdvancedAsync(freeCandidate.UserId, job.Id, freeResume.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        Assert.Null(result.Data);
    }
}
