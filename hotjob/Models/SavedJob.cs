using System.ComponentModel.DataAnnotations;

namespace hotjob.Models;

/// <summary>Job bookmarked by a candidate (many-to-many join).</summary>
public class SavedJob
{
    public int CandidateId { get; set; }

    public int JobPostingId { get; set; }

    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Candidate who bookmarked the job.</summary>
    public Users.Candidate Candidate { get; set; } = default!;

    /// <summary>Bookmarked posting.</summary>
    public Job.JobPosting JobPosting { get; set; } = default!;
}
