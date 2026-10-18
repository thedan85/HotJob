using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Users;

/// <summary>Verification state of a company profile.</summary>
public enum CompanyStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>Company size bracket.</summary>
public enum CompanyScale
{
    LessThan10,
    From10To49,
    From50To199,
    From200To499,
    MoreThan500
}

/// <summary>Company profile that owns job postings and employs employer accounts.</summary>
public class CompanyProfile
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = default!;

    [Url, MaxLength(2048)]
    public string? Website { get; set; }

    [Url, MaxLength(2048)]
    public string? LogoUrl { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(100)]
    public string? Industry { get; set; }

    public CompanyScale? Scale { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public CompanyStatus Status { get; set; } = CompanyStatus.Pending;

    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    public DateTime? VerifiedAt { get; set; }

    /// <summary>Admin who verified this company. Null until reviewed.</summary>
    [MaxLength(255)]
    public string? VerifiedByAdminId { get; set; }

    /// <summary>Admin who verified this company. Null until reviewed.</summary>
    public ApplicationUser? VerifiedBy { get; set; }

    /// <summary>Soft delete flag. Deactivated companies cannot post or manage jobs.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When this company was deactivated by admin.</summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>Admin who deactivated this company.</summary>
    [MaxLength(255)]
    public string? DeactivatedByAdminId { get; set; }

    /// <summary>Employer members of the company.</summary>
    public ICollection<Employer> Employers { get; set; } = new List<Employer>();

    /// <summary>Job postings published by the company.</summary>
    public ICollection<Job.JobPosting> JobPostings { get; set; } = new List<Job.JobPosting>();

    /// <summary>Reviews written about the company.</summary>
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
