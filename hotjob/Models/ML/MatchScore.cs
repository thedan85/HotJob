using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.ML;

/// <summary>ML match score between a job posting and a resume.</summary>
public class MatchScore
{
    public int JobPostingId { get; set; }

    public int ResumeId { get; set; }

    [Range(0, 100)]
    public double MatchPercentage { get; set; }

    [MaxLength(50)]
    public string? AlgorithmUsed { get; set; }

    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Posting the score was computed for.</summary>
    public Job.JobPosting JobPosting { get; set; } = default!;

    /// <summary>Resume the score was computed for.</summary>
    public Resume.Resume Resume { get; set; } = default!;
}
