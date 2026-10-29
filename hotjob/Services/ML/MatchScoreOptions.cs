using System.ComponentModel.DataAnnotations;

namespace hotjob.Services.ML;

/// <summary>Configuration for match scoring and model training (section <c>MatchScore</c>).</summary>
public sealed class MatchScoreOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MatchScore";

    /// <summary>
    /// Minimum number of usable finished applications before a model is trained.
    /// Below it the weighted rule keeps serving scores (contract D9).
    /// </summary>
    [Range(1, 1_000_000)]
    public int MinTrainingRecords { get; set; } = 50;

    /// <summary>Model file, relative to the content root unless rooted.</summary>
    [Required]
    public string ModelPath { get; set; } = "App_Data/models/matchscore.zip";
}
