namespace hotjob.DTOs.Review;

/// <summary>
/// Review item for list views.
/// </summary>
public record ReviewListItemDto(
    int Id,
    int CompanyProfileId,
    string CompanyName,
    int CandidateId,
    string CandidateName,
    int RatingScore,
    string? ReviewText,
    DateTime CreatedAt,
    bool IsDeleted);

/// <summary>
/// Full review details including moderation information.
/// </summary>
public record ReviewDetailDto(
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
    string? DeletionReason);

/// <summary>
/// Company rating statistics.
/// </summary>
public record CompanyRatingStatsDto(
    int CompanyId,
    double AverageRating,
    int TotalReviews);
