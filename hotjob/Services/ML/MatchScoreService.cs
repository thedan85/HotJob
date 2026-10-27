using hotjob.Data;
using hotjob.DTOs.ML;
using hotjob.Models.Application;
using hotjob.Models.ML;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;

namespace hotjob.Services.ML;

/// <summary>
/// Match scoring and training (contract D9). Scores use the FastTree model when an
/// active model file is usable and the weighted rule otherwise; model access is
/// delegated to <see cref="IMatchModelStore"/> so circuits share one loaded model.
/// </summary>
public sealed class MatchScoreService(
    ApplicationDbContext dbContext,
    IMatchModelStore modelStore,
    ICandidatePremiumService premiumService,
    IOptions<MatchScoreOptions> options,
    TimeProvider timeProvider,
    ILogger<MatchScoreService> logger) : IMatchScoreService
{
    /// <summary>Algorithm marker for the cold-start / fallback rule.</summary>
    public const string WeightedRuleAlgorithm = "weighted-rule";

    /// <summary>Algorithm marker for scores produced by the trained model.</summary>
    public const string ModelAlgorithm = "FastTreeBinary";

    private const int MinimumPerClass = 5;
    private const int SplitSeed = 20261027;
    private const double TestFraction = 0.2;

    public async Task<ServiceResult<MatchScoreDto>> GetAsync(
        string? userId,
        int jobId,
        int? resumeId = null,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateIdAsync(userId, cancellationToken);
        if (!candidate.Success)
        {
            return ServiceResult<MatchScoreDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);
        }

        var scored = await ScoreAsync(candidate.Data, jobId, resumeId, cancellationToken);
        return scored.Success
            ? ServiceResult<MatchScoreDto>.Ok(scored.Data!.Score)
            : ServiceResult<MatchScoreDto>.Fail(scored.ErrorType!.Value, scored.Errors);
    }

    public async Task<ServiceResult<AdvancedMatchScoreDto>> GetAdvancedAsync(
        string? userId,
        int jobId,
        int resumeId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateIdAsync(userId, cancellationToken);
        if (!candidate.Success)
        {
            return ServiceResult<AdvancedMatchScoreDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);
        }

        // ponytail: the gate is here, before any breakdown is loaded, so hiding the link
        // in the UI is not the security boundary.
        if (!await premiumService.IsPremiumAsync(userId, cancellationToken))
        {
            return ServiceResult<AdvancedMatchScoreDto>.Fail(
                ServiceErrorType.Unauthorized,
                "Advanced match analysis is a Premium feature.");
        }

        var scored = await ScoreAsync(candidate.Data, jobId, resumeId, cancellationToken);
        if (!scored.Success)
        {
            return ServiceResult<AdvancedMatchScoreDto>.Fail(scored.ErrorType!.Value, scored.Errors);
        }

        var match = scored.Data!;
        if (match.Features is null)
        {
            return ServiceResult<AdvancedMatchScoreDto>.Fail(
                ServiceErrorType.Validation,
                match.Score.Message ?? "A match score is not available for this job.");
        }

        var matched = match.JobSkills
            .Where(skill => match.ResumeYears.ContainsKey(skill.SkillId))
            .Select(skill => new MatchSkillDetailDto(
                skill.SkillId,
                skill.SkillName,
                skill.ImportanceWeight,
                match.ResumeYears[skill.SkillId]))
            .OrderByDescending(skill => skill.ImportanceWeight)
            .ThenBy(skill => skill.SkillName)
            .ToArray();

        var missing = match.JobSkills
            .Where(skill => !match.ResumeYears.ContainsKey(skill.SkillId))
            .Select(skill => new MatchSkillDetailDto(skill.SkillId, skill.SkillName, skill.ImportanceWeight, 0))
            .OrderByDescending(skill => skill.ImportanceWeight)
            .ThenBy(skill => skill.SkillName)
            .ToArray();

        return ServiceResult<AdvancedMatchScoreDto>.Ok(new AdvancedMatchScoreDto(
            jobId,
            match.Score.ResumeId,
            match.Score.MatchPercentage,
            match.Score.AlgorithmUsed ?? WeightedRuleAlgorithm,
            matched,
            missing,
            match.Features.ExperienceGap,
            match.Features.HasAllRequired,
            BuildHints(match, matched.Length, missing)));
    }

    public async Task<ServiceResult<MatchTrainingResultDto>> TrainAsync(
        string? trainedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(trainedByUserId)
            && !await dbContext.Users.AnyAsync(user => user.Id == trainedByUserId, cancellationToken))
        {
            return ServiceResult<MatchTrainingResultDto>.Fail(
                ServiceErrorType.Validation, "Unknown user.");
        }

        var rows = await LoadTrainingRowsAsync(cancellationToken);
        var positives = rows.Count(row => row.Label);
        var negatives = rows.Count - positives;
        var minimum = options.Value.MinTrainingRecords;

        if (rows.Count < minimum || positives < MinimumPerClass || negatives < MinimumPerClass)
        {
            // ponytail: an insufficient-data attempt is a result, not a training run — no
            // model or accuracy exists, so a ModelTrainingLog row would only mislead a later
            // admin screen. The weighted rule keeps serving scores.
            return ServiceResult<MatchTrainingResultDto>.Ok(new MatchTrainingResultDto(
                Trained: false,
                Promoted: false,
                RecordCount: rows.Count,
                Accuracy: null,
                ModelPath: null,
                Message: $"Insufficient data: {rows.Count} usable finished application(s) " +
                    $"({positives} offered, {negatives} rejected/withdrawn); " +
                    $"need at least {minimum} with {MinimumPerClass} per outcome. Weighted rule remains active."));
        }

        var (trainRows, testRows) = SplitStratified(rows);
        var mlContext = new MLContext(seed: SplitSeed);
        var trainData = mlContext.Data.LoadFromEnumerable(trainRows);
        var pipeline = mlContext.Transforms.Concatenate(
                "Features",
                nameof(MatchTrainingRow.MatchedWeightRatio),
                nameof(MatchTrainingRow.MatchedCountRatio),
                nameof(MatchTrainingRow.ExperienceGap),
                nameof(MatchTrainingRow.HasAllRequired))
            .Append(mlContext.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
            {
                LabelColumnName = "Label",
                FeatureColumnName = "Features",
                NumberOfLeaves = 20,
                NumberOfTrees = 100,
                MinimumExampleCountPerLeaf = 5,
                Seed = SplitSeed
            }));

        var model = pipeline.Fit(trainData);
        var predictions = model.Transform(mlContext.Data.LoadFromEnumerable(testRows));
        var metrics = mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");
        var accuracy = Math.Clamp(metrics.Accuracy, 0, 1);

        var active = await dbContext.ModelTrainingLogs
            .AsNoTracking()
            .Where(log => log.IsActive)
            .OrderByDescending(log => log.TrainedAt)
            .ThenByDescending(log => log.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // A log whose file has disappeared cannot score, so its accuracy must not block a
        // recovered model.
        var activeAccuracy = active is not null && File.Exists(modelStore.ModelFilePath)
            ? active.AccuracyScore
            : (double?)null;
        var promote = activeAccuracy is null || accuracy >= activeAccuracy.Value;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (promote)
        {
            modelStore.Save(model, trainData.Schema);
            await dbContext.ModelTrainingLogs
                .Where(log => log.IsActive)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(log => log.IsActive, false),
                    cancellationToken);

            // ExecuteUpdate bypasses the change tracker; detach earlier log instances so a
            // repeated run in the same circuit cannot write stale IsActive values.
            foreach (var tracked in dbContext.ChangeTracker.Entries<ModelTrainingLog>().ToList())
            {
                tracked.State = EntityState.Detached;
            }
        }

        dbContext.ModelTrainingLogs.Add(new ModelTrainingLog
        {
            RecordCount = rows.Count,
            AccuracyScore = accuracy,
            ModelFilePath = promote ? modelStore.ModelFilePath : null,
            TrainedByUserId = string.IsNullOrWhiteSpace(trainedByUserId) ? null : trainedByUserId,
            TrainedAt = now,
            IsActive = promote
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var message = promote
            ? $"Trained on {rows.Count} applications with {accuracy:P1} hold-out accuracy. Model promoted."
            : $"Trained on {rows.Count} applications with {accuracy:P1} hold-out accuracy, " +
                $"below the active model's {activeAccuracy!.Value:P1}. The previous model was kept.";

        logger.LogInformation(
            "Match model training finished: rows {Rows}, accuracy {Accuracy}, promoted {Promoted}.",
            rows.Count, accuracy, promote);

        return ServiceResult<MatchTrainingResultDto>.Ok(new MatchTrainingResultDto(
            Trained: true,
            Promoted: promote,
            RecordCount: rows.Count,
            Accuracy: accuracy,
            ModelPath: promote ? modelStore.ModelFilePath : null,
            Message: message));
    }

    private async Task<ServiceResult<ScoredMatch>> ScoreAsync(
        int candidateId,
        int jobId,
        int? resumeId,
        CancellationToken cancellationToken)
    {
        var resolvedResumeId = await ResolveResumeIdAsync(candidateId, resumeId, cancellationToken);
        if (resolvedResumeId is null)
        {
            return ServiceResult<ScoredMatch>.Fail(ServiceErrorType.NotFound, "Resume not found.");
        }

        var job = await dbContext.JobPostings
            .AsNoTracking()
            .Where(row => row.Id == jobId)
            .Select(row => new { row.Id, row.ExperienceRequired })
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return ServiceResult<ScoredMatch>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        var jobSkills = await dbContext.JobSkills
            .AsNoTracking()
            .Where(skill => skill.JobPostingId == jobId)
            .Select(skill => new JobSkillRow(skill.SkillId, skill.Skill.Name, skill.ImportanceWeight))
            .ToListAsync(cancellationToken);
        if (jobSkills.Count == 0)
        {
            return ServiceResult<ScoredMatch>.Ok(new ScoredMatch(
                new MatchScoreDto(
                    Available: false,
                    JobPostingId: jobId,
                    ResumeId: resolvedResumeId.Value,
                    MatchPercentage: 0,
                    AlgorithmUsed: null,
                    CalculatedAt: null,
                    Message: "No match score is available because this job does not list any required skills."),
                Features: null,
                JobSkills: jobSkills,
                ResumeYears: new Dictionary<int, int>(),
                ExperienceRequired: job.ExperienceRequired));
        }

        var resumeYears = await dbContext.ResumeSkills
            .AsNoTracking()
            .Where(skill => skill.ResumeId == resolvedResumeId.Value)
            .ToDictionaryAsync(
                skill => skill.SkillId,
                skill => skill.YearsOfExperience,
                cancellationToken);

        var features = MatchFeatureExtractor.Extract(
            jobSkills.ToDictionary(skill => skill.SkillId, skill => skill.ImportanceWeight),
            resumeYears,
            job.ExperienceRequired)!;

        var existing = await dbContext.MatchScores.FirstOrDefaultAsync(
            row => row.JobPostingId == jobId && row.ResumeId == resolvedResumeId.Value,
            cancellationToken);

        var activeLog = await dbContext.ModelTrainingLogs
            .AsNoTracking()
            .Where(log => log.IsActive)
            .OrderByDescending(log => log.TrainedAt)
            .ThenByDescending(log => log.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var modelUsable = activeLog is not null && modelStore.IsModelAvailable;
        var expectedAlgorithm = modelUsable ? ModelAlgorithm : WeightedRuleAlgorithm;

        var fresh = existing is not null
            && existing.AlgorithmUsed == expectedAlgorithm
            && (!modelUsable || existing.CalculatedAt >= activeLog!.TrainedAt);
        if (fresh)
        {
            return ServiceResult<ScoredMatch>.Ok(new ScoredMatch(
                ToDto(existing!, jobId, resolvedResumeId.Value),
                features,
                jobSkills,
                resumeYears,
                job.ExperienceRequired));
        }

        double percentage;
        string algorithm;
        if (modelUsable && modelStore.TryPredict(features, out var probability))
        {
            percentage = Math.Clamp(probability * 100, 0, 100);
            algorithm = ModelAlgorithm;
        }
        else
        {
            percentage = Math.Clamp(features.MatchedWeightRatio * 100, 0, 100);
            algorithm = WeightedRuleAlgorithm;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            // ponytail: invalidation deletes with ExecuteDelete, which leaves an earlier
            // loaded row in the change tracker; detach it before re-adding the same key.
            var stale = dbContext.ChangeTracker.Entries<MatchScore>()
                .FirstOrDefault(entry => entry.Entity.JobPostingId == jobId
                    && entry.Entity.ResumeId == resolvedResumeId.Value);
            if (stale is not null)
            {
                stale.State = EntityState.Detached;
            }

            existing = new MatchScore
            {
                JobPostingId = jobId,
                ResumeId = resolvedResumeId.Value
            };
            dbContext.MatchScores.Add(existing);
        }

        existing.MatchPercentage = percentage;
        existing.AlgorithmUsed = algorithm;
        existing.CalculatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ScoredMatch>.Ok(new ScoredMatch(
            ToDto(existing, jobId, resolvedResumeId.Value),
            features,
            jobSkills,
            resumeYears,
            job.ExperienceRequired));
    }

    private async Task<int?> ResolveResumeIdAsync(
        int candidateId,
        int? resumeId,
        CancellationToken cancellationToken)
    {
        if (resumeId is > 0)
        {
            var owned = await dbContext.Resumes
                .AsNoTracking()
                .AnyAsync(
                    resume => resume.Id == resumeId.Value && resume.CandidateId == candidateId,
                    cancellationToken);
            return owned ? resumeId : null;
        }

        return await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.CandidateId == candidateId)
            .OrderByDescending(resume => resume.IsPrimary)
            .ThenByDescending(resume => resume.CreatedAt)
            .ThenByDescending(resume => resume.Id)
            .Select(resume => (int?)resume.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ServiceResult<int>> ResolveCandidateIdAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<int>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");
        }

        var candidateId = await dbContext.Candidates
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => (int?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return candidateId is null
            ? ServiceResult<int>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.")
            : ServiceResult<int>.Ok(candidateId.Value);
    }

    private async Task<List<MatchTrainingRow>> LoadTrainingRowsAsync(CancellationToken cancellationToken)
    {
        var applications = await dbContext.Applications
            .AsNoTracking()
            .Where(application => application.CurrentStatus == ApplicationStatus.Offered
                || application.CurrentStatus == ApplicationStatus.Rejected
                || application.CurrentStatus == ApplicationStatus.Withdrawn)
            .OrderBy(application => application.Id)
            .Select(application => new TrainingApplication(
                application.Id,
                application.JobPostingId,
                application.ResumeId,
                application.CurrentStatus))
            .ToListAsync(cancellationToken);

        if (applications.Count == 0)
        {
            return new List<MatchTrainingRow>();
        }

        var applicationIds = applications.Select(application => application.Id).ToList();
        var histories = await dbContext.ApplicationStatusHistories
            .AsNoTracking()
            .Where(history => applicationIds.Contains(history.ApplicationId))
            .Select(history => new
            {
                history.ApplicationId,
                history.Notes,
                history.ChangedAt,
                history.Id
            })
            .ToListAsync(cancellationToken);

        var latestNoteByApplication = histories
            .GroupBy(history => history.ApplicationId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(history => history.ChangedAt)
                    .ThenByDescending(history => history.Id)
                    .First()
                    .Notes);

        // Auto-rejection on job closure is not a hiring decision, so those rows must not
        // teach the model that closure means "rejected".
        var eligible = applications
            .Where(application => !(application.Status == ApplicationStatus.Rejected
                && latestNoteByApplication.TryGetValue(application.Id, out var note)
                && string.Equals(note, "Job closed", StringComparison.Ordinal)))
            .ToList();

        if (eligible.Count == 0)
        {
            return new List<MatchTrainingRow>();
        }

        var jobIds = eligible.Select(application => application.JobPostingId).Distinct().ToList();
        var resumeIds = eligible.Select(application => application.ResumeId).Distinct().ToList();

        var jobExperiences = await dbContext.JobPostings
            .AsNoTracking()
            .Where(job => jobIds.Contains(job.Id))
            .Select(job => new { job.Id, job.ExperienceRequired })
            .ToListAsync(cancellationToken);
        var jobSkillRows = await dbContext.JobSkills
            .AsNoTracking()
            .Where(skill => jobIds.Contains(skill.JobPostingId))
            .Select(skill => new
            {
                skill.JobPostingId,
                skill.SkillId,
                skill.ImportanceWeight
            })
            .ToListAsync(cancellationToken);
        var resumeSkillRows = await dbContext.ResumeSkills
            .AsNoTracking()
            .Where(skill => resumeIds.Contains(skill.ResumeId))
            .Select(skill => new
            {
                skill.ResumeId,
                skill.SkillId,
                skill.YearsOfExperience
            })
            .ToListAsync(cancellationToken);

        var experienceByJob = jobExperiences.ToDictionary(job => job.Id, job => job.ExperienceRequired);
        var jobSkillsByJob = jobSkillRows
            .GroupBy(skill => skill.JobPostingId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(skill => skill.SkillId, skill => skill.ImportanceWeight));
        var resumeYearsByResume = resumeSkillRows
            .GroupBy(skill => skill.ResumeId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(skill => skill.SkillId, skill => skill.YearsOfExperience));

        var rows = new List<MatchTrainingRow>(eligible.Count);
        foreach (var application in eligible)
        {
            if (!experienceByJob.TryGetValue(application.JobPostingId, out var experienceRequired)
                || !jobSkillsByJob.TryGetValue(application.JobPostingId, out var jobSkillWeights)
                || !resumeYearsByResume.TryGetValue(application.ResumeId, out var resumeYears))
            {
                continue;
            }

            var features = MatchFeatureExtractor.Extract(jobSkillWeights, resumeYears, experienceRequired);
            if (features is null)
            {
                continue;
            }

            rows.Add(new MatchTrainingRow
            {
                Label = application.Status == ApplicationStatus.Offered,
                MatchedWeightRatio = (float)features.MatchedWeightRatio,
                MatchedCountRatio = (float)features.MatchedCountRatio,
                ExperienceGap = (float)features.ExperienceGap,
                HasAllRequired = features.HasAllRequired ? 1f : 0f
            });
        }

        return rows;
    }

    private static (List<MatchTrainingRow> Train, List<MatchTrainingRow> Test) SplitStratified(
        IReadOnlyList<MatchTrainingRow> rows)
    {
        var random = new Random(SplitSeed);
        var train = new List<MatchTrainingRow>(rows.Count);
        var test = new List<MatchTrainingRow>(rows.Count);

        // ponytail: stratify so a small minority class (needed to train at all) also
        // appears in the hold-out; a plain shuffle could put every offer in one side.
        foreach (var group in rows.GroupBy(row => row.Label).OrderBy(group => group.Key))
        {
            var shuffled = group.ToList();
            for (var index = shuffled.Count - 1; index > 0; index--)
            {
                var swap = random.Next(index + 1);
                (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
            }

            var testCount = Math.Clamp(
                (int)Math.Round(shuffled.Count * TestFraction, MidpointRounding.AwayFromZero),
                1,
                shuffled.Count - 1);
            test.AddRange(shuffled.Take(testCount));
            train.AddRange(shuffled.Skip(testCount));
        }

        return (train, test);
    }

    private static MatchScoreDto ToDto(MatchScore score, int jobId, int resumeId) =>
        new(
            Available: true,
            JobPostingId: jobId,
            ResumeId: resumeId,
            MatchPercentage: score.MatchPercentage,
            AlgorithmUsed: score.AlgorithmUsed,
            CalculatedAt: score.CalculatedAt,
            Message: null);

    private static IReadOnlyList<string> BuildHints(
        ScoredMatch match,
        int matchedCount,
        IReadOnlyList<MatchSkillDetailDto> missing)
    {
        var hints = new List<string>();
        if (missing.Count > 0)
        {
            var names = string.Join(", ", missing.Select(skill => skill.SkillName));
            hints.Add($"Add {names} to your CV — the employer lists these as required skills.");
        }

        var experienceGap = match.Features!.ExperienceGap;
        if (experienceGap < 0)
        {
            var resumeYears = Math.Max(0, experienceGap + match.ExperienceRequired);
            hints.Add(
                $"This role asks for {match.ExperienceRequired} year(s) of experience and your CV lists " +
                $"{resumeYears:0.#}. Highlight relevant projects or responsibilities that close the gap.");
        }

        if (missing.Count == 0 && experienceGap >= 0)
        {
            hints.Add("Your CV covers every required skill and the requested experience. " +
                "Use your cover letter to show measurable results.");
        }
        else if (matchedCount > 0)
        {
            hints.Add("Lead with your strongest matched skills in the first lines of your CV summary.");
        }

        return hints;
    }

    private sealed record JobSkillRow(int SkillId, string SkillName, double ImportanceWeight);

    private sealed record TrainingApplication(
        int Id,
        int JobPostingId,
        int ResumeId,
        ApplicationStatus Status);

    private sealed record ScoredMatch(
        MatchScoreDto Score,
        MatchFeatures? Features,
        IReadOnlyList<JobSkillRow> JobSkills,
        IReadOnlyDictionary<int, int> ResumeYears,
        int ExperienceRequired);
}
