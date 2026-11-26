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
    int PendingReportCount);

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
    [MaxLength(500)] string? Reason = null);

/// <summary>One user report against a review, as the moderator sees it.</summary>
public record ReviewReportDto(
    int Id,
    int ReviewId,
    string ReporterEmail,
    string Reason,
    string? Details,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolutionNote);

public record DismissReportsRequest(
    [MaxLength(500)] string? Note = null);
