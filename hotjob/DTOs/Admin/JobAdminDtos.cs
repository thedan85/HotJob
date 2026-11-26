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
    string? ModerationReason = null);

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
