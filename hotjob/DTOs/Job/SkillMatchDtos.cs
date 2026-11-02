using hotjob.Models.Job;

namespace hotjob.DTOs.Job;

public sealed record SkillMatchJobListItemDto(
    int Id,
    string Title,
    string CompanyName,
    string? Location,
    decimal? SalaryMin,
    decimal? SalaryMax,
    int ExperienceRequired,
    JobType JobType,
    DateTime? ExpiryDate,
    int MatchedSkillCount,
    IReadOnlyList<MatchedSkillDto> MatchedSkills,
    double? MatchPercentage,
    DateTime CreatedAt,
    DateTime? FeaturedUntil);

public sealed record MatchedSkillDto(int SkillId, string SkillName);
