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
    int ApplicationCount);

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
    string? ReviewedByAdminId);

public record RejectJobRequest(
    [Required, MinLength(1), MaxLength(500)] string Reason);
