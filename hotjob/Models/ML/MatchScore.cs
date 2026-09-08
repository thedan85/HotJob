namespace hotjob.Models.ML;

public class MatchScore
{
    public int JobPostingId { get; set; }
    public int ResumeId { get; set; }
    public double MatchPercentage { get; set; }
    public string? AlgorithmUsed { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    public Job.JobPosting JobPosting { get; set; } = default!;
    public Resume.Resume Resume { get; set; } = default!;
}
