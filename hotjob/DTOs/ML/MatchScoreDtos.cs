namespace hotjob.DTOs.ML;

/// <summary>
/// The four model inputs extracted from a resume/job pair (contract D9).
/// Ratios are 0–1; the experience gap is not clamped.
/// </summary>
public sealed record MatchFeatures(
    double MatchedWeightRatio,
    double MatchedCountRatio,
    double ExperienceGap,
    bool HasAllRequired);

/// <summary>
/// Basic match result shown to every signed-in candidate. When
/// <see cref="Available"/> is false the UI hides the badge and shows
/// <see cref="Message"/> instead.
/// </summary>
public sealed record MatchScoreDto(
    bool Available,
    int JobPostingId,
    int ResumeId,
    double MatchPercentage,
    string? AlgorithmUsed,
    DateTime? CalculatedAt,
    string? Message);

/// <summary>A required job skill, optionally with the candidate's years on that skill.</summary>
public sealed record MatchSkillDetailDto(
    int SkillId,
    string SkillName,
    double ImportanceWeight,
    int ResumeYears);

/// <summary>Premium-only breakdown of a match with concrete improvement hints.</summary>
public sealed record AdvancedMatchScoreDto(
    int JobPostingId,
    int ResumeId,
    double MatchPercentage,
    string AlgorithmUsed,
    IReadOnlyList<MatchSkillDetailDto> MatchedSkills,
    IReadOnlyList<MatchSkillDetailDto> MissingSkills,
    double ExperienceGap,
    bool HasAllRequired,
    IReadOnlyList<string> ImprovementHints);

/// <summary>Outcome of one model training run, shaped for a future admin screen.</summary>
public sealed record MatchTrainingResultDto(
    bool Trained,
    bool Promoted,
    int RecordCount,
    double? Accuracy,
    string? ModelPath,
    string Message);
