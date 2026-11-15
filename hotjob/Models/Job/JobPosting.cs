using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Job;

/// <summary>Lifecycle state of a job posting.</summary>
public enum JobStatus
{
    Draft,
    PendingApproval,
    Approved,
    Rejected,
    Published,
    Closed,
    Expired
}

/// <summary>Employment type of a job posting.</summary>
public enum JobType
{
    FullTime,
    PartTime,
    Contract,
    Internship,
    Freelance,
    Remote
}

/// <summary>Job posting published by a company.</summary>
public class JobPosting
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    public int? CreatedByEmployerId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = default!;

    [Required, MaxLength(10000)]
    public string Description { get; set; } = default!;

    [Range(0, double.MaxValue)]
    public decimal? SalaryMin { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? SalaryMax { get; set; }

    [Range(0, 50)]
    public int ExperienceRequired { get; set; }

    public JobType JobType { get; set; } = JobType.FullTime;

    public DateTime? ExpiryDate { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this job was submitted for approval.</summary>
    public DateTime? SubmittedForApprovalAt { get; set; }

    /// <summary>When this job was approved/rejected by admin.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Admin who reviewed this job.</summary>
    [MaxLength(255)]
    public string? ReviewedByAdminId { get; set; }

    /// <summary>Rejection reason if status is Rejected.</summary>
    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    /// <summary>Company that owns this posting.</summary>
    public Users.CompanyProfile Company { get; set; } = default!;

    /// <summary>Employer who created this posting. Null if the author left.</summary>
    public Users.Employer? CreatedBy { get; set; }

    /// <summary>Admin who reviewed this job posting.</summary>
    public Users.ApplicationUser? ReviewedBy { get; set; }

    /// <summary>Required skills with importance weights.</summary>
    public ICollection<JobSkill> JobSkills { get; set; } = new List<JobSkill>();

    /// <summary>Category mappings of this posting.</summary>
    public ICollection<JobCategoryMapping> CategoryMappings { get; set; } = new List<JobCategoryMapping>();

    /// <summary>Applications submitted to this posting.</summary>
    public ICollection<Application.Application> Applications { get; set; } = new List<Application.Application>();

    /// <summary>Candidates that bookmarked this posting.</summary>
    public ICollection<SavedJob> SavedBy { get; set; } = new List<SavedJob>();

    /// <summary>ML match scores computed for this posting.</summary>
    public ICollection<ML.MatchScore> MatchScores { get; set; } = new List<ML.MatchScore>();
}
