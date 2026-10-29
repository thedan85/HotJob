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
    public static MatchFeatures? Extract(
        IReadOnlyDictionary<int, double> jobSkillWeights,
        IReadOnlyDictionary<int, int> resumeSkillYears,
        int experienceRequired)
    {
        if (jobSkillWeights.Count == 0)
        {
            return null;
        }

        var matchedSkillIds = jobSkillWeights.Keys
            .Where(resumeSkillYears.ContainsKey)
            .ToArray();

        var totalWeight = jobSkillWeights.Values.Sum();
        var matchedWeight = matchedSkillIds.Sum(skillId => jobSkillWeights[skillId]);

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
