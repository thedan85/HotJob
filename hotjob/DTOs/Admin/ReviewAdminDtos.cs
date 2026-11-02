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
    string? DeletedByAdminId);

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

public record DeleteReviewRequest(
    [Required] string AdminUserId,
    [MaxLength(500)] string? Reason = null);
