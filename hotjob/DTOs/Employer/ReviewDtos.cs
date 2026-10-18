using hotjob.DTOs.Job;

namespace hotjob.DTOs.Employer;

/// <summary>Single review item in the company's review list.</summary>
public sealed record ReviewListItemDto(
    int Id,
    int CandidateId,
    string? CandidateName,
    int RatingScore,
    string? ReviewText,
    DateTime CreatedAt);

/// <summary>Aggregated rating statistics for a company.</summary>
public sealed class CompanyRatingStatsDto
{
    public int TotalReviews { get; init; }
    public double AverageRating { get; init; }
    public int FiveStarCount { get; init; }
    public int FourStarCount { get; init; }
    public int ThreeStarCount { get; init; }
    public int TwoStarCount { get; init; }
    public int OneStarCount { get; init; }
}
