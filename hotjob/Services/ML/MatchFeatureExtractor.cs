using hotjob.DTOs.ML;

namespace hotjob.Services.ML;

/// <summary>
/// Pure feature extraction for one resume/job pair (contract D9). Kept separate
/// from the database so the safe-division rules are directly testable.
/// </summary>
public static class MatchFeatureExtractor
{
    /// <summary>
    /// Extracts the four model features. Returns <c>null</c> when the job has no
    /// required skills: there is no denominator, so no score is available.
    /// </summary>
    /// <param name="jobSkillWeights">Required skills mapped to their importance weight.</param>
    /// <param name="resumeSkillYears">Resume skills mapped to years of experience.</param>
    /// <param name="experienceRequired">Years requested by the posting.</param>
    /// <param name="minimumYears">
    /// Per-skill years required by the posting. A missing entry or 0 means presence is
    /// enough. Years above the minimum earn no extra credit.
    /// </param>
    public static MatchFeatures? Extract(
        IReadOnlyDictionary<int, double> jobSkillWeights,
        IReadOnlyDictionary<int, int> resumeSkillYears,
        int experienceRequired,
        IReadOnlyDictionary<int, int>? minimumYears = null)
    {
        if (jobSkillWeights.Count == 0)
        {
            return null;
        }

        var matchedSkillIds = jobSkillWeights.Keys
            .Where(resumeSkillYears.ContainsKey)
            .ToArray();

        var totalWeight = jobSkillWeights.Values.Sum();

        // Partial credit only when the posting names a per-skill minimum. Extra years
        // above it are capped at full credit so a senior is not penalized.
        var matchedWeight = 0d;
        foreach (var skillId in matchedSkillIds)
        {
            var requiredYears = minimumYears is not null
                && minimumYears.TryGetValue(skillId, out var years)
                ? years
                : 0;
            var credit = requiredYears > 0
                ? Math.Min(1d, (double)resumeSkillYears[skillId] / requiredYears)
                : 1d;
            matchedWeight += jobSkillWeights[skillId] * credit;
        }

        // ponytail: a job whose weights all sum to zero still needs a ranking signal,
        // so the contract falls back to the unweighted count ratio.
        var matchedWeightRatio = totalWeight > 0
            ? matchedWeight / totalWeight
            : (double)matchedSkillIds.Length / jobSkillWeights.Count;

        var experienceGap = resumeSkillYears.Values.Sum() - experienceRequired;

        return new MatchFeatures(
            matchedWeightRatio,
            (double)matchedSkillIds.Length / jobSkillWeights.Count,
            experienceGap,
            matchedSkillIds.Length == jobSkillWeights.Count);
    }
}
