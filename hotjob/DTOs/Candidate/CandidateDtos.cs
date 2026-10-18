using hotjob.DTOs.Employer;
using hotjob.Models.Application;

namespace hotjob.DTOs.Candidate;

/// <summary>A candidate's own application, with its status timeline.</summary>
public sealed record MyApplicationDto(
    int ApplicationId,
    string JobTitle,
    string CompanyName,
    string ResumeTitle,
    ApplicationStatus Status,
    DateTime AppliedAt,
    IReadOnlyList<HistoryDto> History);

/// <summary>An in-app notification addressed to the candidate.</summary>
public sealed record MyNotificationDto(
    int Id,
    string Title,
    string? Content,
    bool IsRead,
    DateTime CreatedAt);
