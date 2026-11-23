using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

/// <summary>Paginated list item for a review.</summary>
public record ReviewListItemDto(
    int Id,
    int CompanyProfileId,
    string CompanyName,
    int CandidateId,
    string CandidateName,
    int RatingScore,
    string? ReviewText,
    DateTime CreatedAt,
    bool IsDeleted,
    DateTime? DeletedAt,
    string? DeletedByAdminId,
    string? DeletionReason);

/// <summary>Detailed view of a review including full candidate info.</summary>
public record ReviewDetailDto(
    int Id,
    int CompanyProfileId,
    string CompanyName,
    int CandidateId,
    string CandidateEmail,
    string CandidateName,
    int RatingScore,
    string? ReviewText,
    DateTime CreatedAt,
    bool IsDeleted,
    DateTime? DeletedAt,
    string? DeletedByAdminId,
    string? DeletionReason);

/// <summary>Optional body for hiding a review. The admin is taken from the token.</summary>
public record DeleteReviewRequest(
    [MaxLength(500)] string? Reason = null);
