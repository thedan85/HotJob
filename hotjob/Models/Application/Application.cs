using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Application;

/// <summary>Lifecycle state of a job application.</summary>
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

/// <summary>Job application submitted by a candidate with a resume snapshot.</summary>
public class Application
{
    public int Id { get; set; }

    public int JobPostingId { get; set; }

    public int ResumeId { get; set; }

    public int CandidateId { get; set; }

    [MaxLength(2000)]
    public string? CoverLetter { get; set; }

    public ApplicationStatus CurrentStatus { get; set; } = ApplicationStatus.Pending;

    [Url, MaxLength(2048)]
    public string? SnapshotFileUrl { get; set; }

    [MaxLength(2000)]
    public string? SnapshotSummary { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Posting the candidate applied to.</summary>
    public Job.JobPosting JobPosting { get; set; } = default!;

    /// <summary>Resume used for this application.</summary>
    public Resume.Resume Resume { get; set; } = default!;

    /// <summary>Candidate who applied.</summary>
    public Users.Candidate Candidate { get; set; } = default!;

    /// <summary>Status change history.</summary>
    public ICollection<ApplicationStatusHistory> StatusHistory { get; set; } = new List<ApplicationStatusHistory>();

    /// <summary>Interviews scheduled for this application.</summary>
    public ICollection<Interview> Interviews { get; set; } = new List<Interview>();
}
