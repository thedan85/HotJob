using System.ComponentModel.DataAnnotations;

namespace hotjob.Models;

/// <summary>Review a candidate wrote about a company.</summary>
public class Review
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    public int CandidateId { get; set; }

    [Range(1, 5)]
    public int RatingScore { get; set; }

    [MaxLength(2000)]
    public string? ReviewText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft delete flag for admin moderation.</summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>When this review was deleted/hidden by admin.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Admin user who deleted this review.</summary>
    [MaxLength(255)]
    public string? DeletedByAdminId { get; set; }

    /// <summary>Reason for deletion (policy violation, spam, etc.).</summary>
    [MaxLength(500)]
    public string? DeletionReason { get; set; }

    /// <summary>Company being reviewed.</summary>
    public Users.CompanyProfile Company { get; set; } = default!;

    /// <summary>Candidate who wrote the review.</summary>
    public Users.Candidate Candidate { get; set; } = default!;

    /// <summary>Admin who deleted this review.</summary>
    public Users.ApplicationUser? DeletedBy { get; set; }
}
