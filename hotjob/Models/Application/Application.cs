namespace hotjob.Models.Application;

public enum ApplicationStatus
{
    Pending,
    Viewed,
    Shortlisted,
    Interviewing,
    Offered,
    Rejected,
    Withdrawn
}

public class Application
{
    public int Id { get; set; }
    public int JobPostingId { get; set; }
    public int ResumeId { get; set; }
    public int CandidateId { get; set; }
    public string? CoverLetter { get; set; }
    public ApplicationStatus CurrentStatus { get; set; } = ApplicationStatus.Pending;
    public string? SnapshotFileUrl { get; set; }
    public string? SnapshotSummary { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    public Job.JobPosting JobPosting { get; set; } = default!;
    public Resume.Resume Resume { get; set; } = default!;
    public Users.Candidate Candidate { get; set; } = default!;
    public ICollection<ApplicationStatusHistory> StatusHistory { get; set; } = new List<ApplicationStatusHistory>();
    public ICollection<Interview> Interviews { get; set; } = new List<Interview>();
}
