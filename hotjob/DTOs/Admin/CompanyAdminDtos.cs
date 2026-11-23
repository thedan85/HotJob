using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

/// <summary>Paginated list item for a company profile, with moderation-relevant aggregates.</summary>
/// <param name="ActiveMemberCount">Employers with an active membership whose account is active.</param>
/// <param name="JobCount">All job postings of the company, any status.</param>
/// <param name="ReviewCount">Non-deleted reviews only.</param>
/// <param name="AverageRating">Average of non-deleted review scores, rounded to 2 decimals; null when there are none.</param>
public record CompanyListItemDto(
    int Id,
    string CompanyName,
    string? Industry,
    string? Location,
    string? LogoUrl,
    string Status,
    string? RejectionReason,
    DateTime? VerifiedAt,
    bool IsActive,
    int ActiveMemberCount,
    int JobCount,
    int ReviewCount,
    double? AverageRating);

/// <summary>Detailed view of a company profile for admin moderation.</summary>
/// <param name="OwnerName">Full name of the owner employer, if any.</param>
/// <param name="OwnerEmail">Email of the owner employer, if any.</param>
/// <param name="ReviewCount">Non-deleted reviews only.</param>
/// <param name="AverageRating">Average of non-deleted review scores, rounded to 2 decimals; null when there are none.</param>
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
    string? DeactivatedByAdminId,
    string? DeactivationReason,
    string? OwnerName,
    string? OwnerEmail,
    int ActiveMemberCount,
    int PendingMemberCount,
    int JobCount,
    int PendingApprovalJobCount,
    int PublishedJobCount,
    int ReviewCount,
    double? AverageRating);

/// <summary>Body for rejecting (or revoking approval of) a company. The admin is taken from the token.</summary>
public record RejectCompanyRequest(
    [Required, MaxLength(500)] string Reason);

/// <summary>Optional body for deactivating a company. The admin is taken from the token.</summary>
public record DeactivateCompanyRequest(
    [MaxLength(500)] string? Reason = null);
