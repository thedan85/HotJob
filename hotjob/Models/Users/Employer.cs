using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Users;

/// <summary>Membership state of an employer inside a company.</summary>
public enum MembershipStatus
{
    Pending,
    Active,
    Rejected
}

/// <summary>Employer profile linked one-to-one to an application user and to a company.</summary>
public class Employer
{
    public int Id { get; set; }

    [MaxLength(255)]
    public string UserId { get; set; } = default!;

    public int CompanyProfileId { get; set; }

    [Required, MaxLength(100)]
    public string FullName { get; set; } = default!;

    [MaxLength(100)]
    public string? Position { get; set; }

    public bool IsOwner { get; set; }

    public MembershipStatus MembershipStatus { get; set; } = MembershipStatus.Pending;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft delete flag. Deactivated employers cannot access the system.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When this employer was deactivated by admin.</summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>Admin who deactivated this employer account.</summary>
    [MaxLength(255)]
    public string? DeactivatedByAdminId { get; set; }

    /// <summary>Reason for deactivation.</summary>
    [MaxLength(500)]
    public string? DeactivationReason { get; set; }

    /// <summary>Identity user owning this profile.</summary>
    public ApplicationUser User { get; set; } = default!;

    /// <summary>Company the employer belongs to.</summary>
    public CompanyProfile Company { get; set; } = default!;

    /// <summary>Job postings created by this employer.</summary>
    public ICollection<Job.JobPosting> CreatedJobs { get; set; } = new List<Job.JobPosting>();
}
