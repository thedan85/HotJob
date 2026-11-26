using System.ComponentModel.DataAnnotations;

namespace hotjob.Models;

/// <summary>Why a user reported a review. Persisted as int; values are pinned.</summary>
public enum ReviewReportReason
{
    Spam = 0,
    Offensive = 1,
    FalseInformation = 2,
    PersonalData = 3,
    Other = 4
}

/// <summary>Moderation outcome of a report. Persisted as int; values are pinned.</summary>
public enum ReviewReportStatus
{
    /// <summary>Waiting for an administrator.</summary>
    Pending = 0,

    /// <summary>An administrator agreed and hid the review.</summary>
    Upheld = 1,

    /// <summary>An administrator kept the review visible.</summary>
    Dismissed = 2
}

/// <summary>
/// A signed-in user's report that a company review breaks the rules. One row per (review, reporter);
/// rows are kept after resolution so dismissed reviews are not re-litigated and decisions stay traceable.
/// </summary>
public class ReviewReport
{
    public int Id { get; set; }

    public int ReviewId { get; set; }

    [Required, MaxLength(255)]
    public string ReporterUserId { get; set; } = default!;

    public ReviewReportReason Reason { get; set; }

    [MaxLength(500)]
    public string? Details { get; set; }

    public ReviewReportStatus Status { get; set; } = ReviewReportStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }

    [MaxLength(255)]
    public string? ResolvedByAdminId { get; set; }

    [MaxLength(500)]
    public string? ResolutionNote { get; set; }

    public Review Review { get; set; } = default!;

    public Users.ApplicationUser Reporter { get; set; } = default!;

    public Users.ApplicationUser? ResolvedBy { get; set; }
}
