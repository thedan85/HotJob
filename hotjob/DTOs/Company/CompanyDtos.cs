using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Company;

public sealed class CompanyListQuery
{
    public string? Search { get; set; }
    public IReadOnlyList<string> Industries { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Locations { get; set; } = Array.Empty<string>();
    public int Page { get; set; } = 1;
}

public sealed record CompanyListItemDto(
    int Id,
    string CompanyName,
    string? Industry,
    string? Location,
    Models.Users.CompanyScale? Scale,
    string? LogoUrl,
    string? Description,
    double? AverageRating = null,
    int ReviewCount = 0);

public sealed record CompanyDetailDto(
    int Id,
    string CompanyName,
    string? Industry,
    string? Location,
    Models.Users.CompanyScale? Scale,
    string? LogoUrl,
    string? Description,
    string? Website,
    string? OwnerName,
    double? AverageRating = null,
    int ReviewCount = 0);

public sealed record CompanyReviewDto(
    int Id,
    int RatingScore,
    string? ReviewText,
    string ReviewerName,
    DateTime CreatedAt);

/// <summary>A review as its author sees it, including whether a moderator hid it.</summary>
public sealed record MyReviewDto(
    int Id,
    int CompanyId,
    string CompanyName,
    int RatingScore,
    string? ReviewText,
    DateTime CreatedAt,
    bool IsHidden);

public sealed record CompanyFilterOptionsDto(
    IReadOnlyList<string> Industries,
    IReadOnlyList<string> Locations);

public sealed class CreateReviewRequest
{
    [Range(1, 5)]
    public int RatingScore { get; set; } = 5;

    [MaxLength(2000)]
    public string? ReviewText { get; set; }
}
