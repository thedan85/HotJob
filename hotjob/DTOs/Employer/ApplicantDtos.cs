using System.ComponentModel.DataAnnotations;
using hotjob.Models.Application;

namespace hotjob.DTOs.Employer;

/// <summary>Query parameters for listing owned applications.</summary>
public sealed class ApplicantListQuery
{
    public int? JobPostingId { get; set; }
    public ApplicationStatus? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>Server-paginated wrapper.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public sealed record ApplicantListItemResponse(
    int ApplicationId,
    int JobPostingId,
    string JobTitle,
    int CandidateId,
    string CandidateFullName,
    string? CandidateEmail,
    int ResumeId,
    string ResumeTitle,
    ApplicationStatus CurrentStatus,
    DateTime AppliedAt);

public sealed class ApplicantDetailResponse
{
    public int ApplicationId { get; set; }
    public int JobPostingId { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public int CandidateId { get; set; }
    public string CandidateFullName { get; set; } = string.Empty;
    public string? CandidateEmail { get; set; }
    public int ResumeId { get; set; }
    public string? CoverLetter { get; set; }
    public ApplicationStatus CurrentStatus { get; set; }
    public string? SnapshotFileUrl { get; set; }
    public string? SnapshotSummary { get; set; }
    public DateTime AppliedAt { get; set; }
    public ResumeDetailDto Resume { get; set; } = new();
    public IReadOnlyList<HistoryDto> StatusHistory { get; set; } = Array.Empty<HistoryDto>();
    public IReadOnlyList<InterviewDto> Interviews { get; set; } = Array.Empty<InterviewDto>();
}

public sealed class ResumeDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? FileUrl { get; set; }
    public bool IsPrimary { get; set; }
    public IReadOnlyList<EducationDto> Educations { get; set; } = Array.Empty<EducationDto>();
    public IReadOnlyList<WorkExperienceDto> WorkExperiences { get; set; } = Array.Empty<WorkExperienceDto>();
    public IReadOnlyList<ResumeSkillDto> Skills { get; set; } = Array.Empty<ResumeSkillDto>();
}

public sealed record EducationDto(
    int Id,
    string SchoolName,
    string? Major,
    DateOnly? StartDate,
    DateOnly? EndDate,
    double? Gpa,
    string? Description);

public sealed record WorkExperienceDto(
    int Id,
    string CompanyName,
    string? JobTitle,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Description);

public sealed record ResumeSkillDto(
    int SkillId,
    string SkillName,
    int YearsOfExperience);

public sealed record HistoryDto(
    int Id,
    ApplicationStatus Status,
    string? ChangedByUserId,
    string? Notes,
    DateTime ChangedAt);

public sealed record InterviewDto(
    int Id,
    int ApplicationId,
    DateTime InterviewDate,
    string? LocationOrLink,
    InterviewType? InterviewType,
    string? Notes,
    InterviewStatus Status);

public sealed class UpdateStatusRequest
{
    /// <summary>Target status. Employer values: Viewed, Shortlisted, Interviewing, Offered, Rejected. Withdrawn is candidate-only.</summary>
    [Required]
    [EnumDataType(typeof(ApplicationStatus))]
    public ApplicationStatus Status { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public sealed class CreateInterviewRequest
{
    /// <summary>Must be in the future (UTC). Example: 2026-10-05T09:00:00Z.</summary>
    [Required]
    public DateTime InterviewDate { get; set; }

    [MaxLength(2048)]
    public string? LocationOrLink { get; set; }

    [EnumDataType(typeof(InterviewType))]
    public InterviewType? InterviewType { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}

public sealed class UpdateInterviewRequest
{
    public DateTime? InterviewDate { get; set; }

    [MaxLength(2048)]
    public string? LocationOrLink { get; set; }

    [EnumDataType(typeof(InterviewType))]
    public InterviewType? InterviewType { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    [EnumDataType(typeof(InterviewStatus))]
    public InterviewStatus? Status { get; set; }
}

public sealed class SendNotificationRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Content { get; set; }
}

public sealed record InterviewResponse(
    int Id,
    int ApplicationId,
    DateTime InterviewDate,
    string? LocationOrLink,
    InterviewType? InterviewType,
    string? Notes,
    InterviewStatus Status);

public sealed record NotificationResponse(
    int Id,
    string UserId,
    string Title,
    string? Content,
    bool IsRead,
    DateTime CreatedAt);
