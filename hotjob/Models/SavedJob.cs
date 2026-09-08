namespace hotjob.Models;

public class SavedJob
{
    public int CandidateId { get; set; }
    public int JobPostingId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    public Users.Candidate Candidate { get; set; } = default!;
    public Job.JobPosting JobPosting { get; set; } = default!;
}
