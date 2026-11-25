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
    DateTime? RejectedAt);

public record ApproveJobRequest(
    [Required] string AdminUserId);

public record RejectJobRequest(
    [Required] string AdminUserId,
    [Required, MinLength(1), MaxLength(500)] string Reason);
