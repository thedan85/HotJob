using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

/// <summary>Paginated list item for a job posting.</summary>
public record JobListItemDto(
    int Id,
    string Title,
    string CompanyName,
    string Status,
    string? City,
    DateTime? ExpiresAt);

/// <summary>Detailed view of a job posting.</summary>
/// <param name="ApprovedAt">Admin review time when the job is in an approved-or-later state; otherwise null.</param>
/// <param name="RejectedAt">Admin review time when the job is currently rejected; otherwise null.</param>
/// <param name="CompanyReviewedAt">When the company owner approved a member-created job (two-step approval).</param>
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
    string? ReviewedByAdminId,
    string? RejectionReason,
    DateTime? CompanyReviewedAt);

/// <summary>Body for rejecting a job posting. The admin is taken from the token.</summary>
public record RejectJobRequest(
    [Required, MaxLength(500)] string Reason);
