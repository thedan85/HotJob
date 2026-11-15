using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

/// <summary>Paginated list item for a company profile.</summary>
public record CompanyListItemDto(
    int Id,
    string CompanyName,
    string? Industry,
    string? Location,
    string? LogoUrl,
    string Status,
    string? RejectionReason,
    DateTime? VerifiedAt);

/// <summary>Detailed view of a company profile.</summary>
public record CompanyDetailDto(
    int Id,
    string CompanyName,
    string? Website,
    string? LogoUrl,
    string? Location,
    string? Industry,
    int? Scale,
    string? Description,
    string Status,
    string? RejectionReason,
    DateTime? VerifiedAt,
    string? VerifiedByAdminId,
    bool IsActive,
    DateTime? DeactivatedAt,
    string? DeactivationReason);

public record ApproveCompanyRequest(
    [Required] string AdminUserId);

public record RejectCompanyRequest(
    [Required] string AdminUserId,
    [Required, MinLength(1), MaxLength(500)] string Reason);
