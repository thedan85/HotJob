using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

/// <summary>Paginated list item for a job posting.</summary>
public record JobListItemDto(
    int Id,
    string Title,
    string CompanyName,
    string Status,
    string? City,
    DateTime? ExpiresAt,
    DateTime CreatedAt,
    DateTime? SubmittedForApprovalAt,
    int ApplicationCount,
    DateTime? ModeratedAt = null,
    string? ModerationReason = null,
    int CompanyProfileId = 0,
    DateTime? FeaturedUntil = null);

/// <summary>Filters for the admin job list. Every field is optional.</summary>
public sealed class AdminJobQuery
{
    /// <summary>Matches the title or the company name.</summary>
    public string? Search { get; init; }

    public hotjob.Models.Job.JobStatus? Status { get; init; }

    public int? CompanyId { get; init; }

    /// <summary>True: featured right now; false: not featured.</summary>
    public bool? IsFeatured { get; init; }

    /// <summary>True: removed by an admin; false: never removed.</summary>
    public bool? IsModerated { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

/// <summary>Detailed view of a job posting.</summary>
public record JobDetailDto(
    int Id,
    string Title,
    string Description,
    string CompanyName,
    int CompanyProfileId,
    string Status,
    string? City,
    DateTime? PostedAt,
    DateTime? ExpiresAt,
    DateTime? SubmittedForApprovalAt,
    DateTime? ApprovedAt,
    DateTime? RejectedAt,
    string? RejectionReason,
    string? ReviewedByAdminId,
    DateTime? ModeratedAt = null,
    string? ModerationReason = null);

/// <summary>Why an admin is taking a posting down; shown to the employer.</summary>
public record RemoveJobRequest(
    [Required, MinLength(1), MaxLength(500)] string Reason);

public record RejectJobRequest(
    [Required, MinLength(1), MaxLength(500)] string Reason);
