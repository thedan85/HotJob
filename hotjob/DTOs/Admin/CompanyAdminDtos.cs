using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

/// <summary>Paginated list item for a company profile.</summary>
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
    int EmployerCount);

/// <summary>Detailed view of a company profile.</summary>
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
    string? DeactivationReason);

public record RejectCompanyRequest(
    [Required, MinLength(1), MaxLength(500)] string Reason);

/// <summary>Everything the admin company page shows: profile, plan, members, jobs, reviews and review history.</summary>
public sealed record AdminCompanyOverviewDto(
    CompanyDetailDto Profile,
    AdminCompanyPlanDto? Plan,
    IReadOnlyList<AdminCompanyMemberDto> Members,
    IReadOnlyList<AdminJobStatusCountDto> JobCounts,
    IReadOnlyList<AdminCompanyJobDto> RecentJobs,
    int ReviewCount,
    double? AverageRating,
    IReadOnlyList<AdminAuditLogDto> History);

public sealed record AdminCompanyPlanDto(
    string PlanCode,
    string PlanName,
    int JobQuota,
    int ActiveJobCount,
    DateTime? PlanExpiresAt);

public sealed record AdminCompanyMemberDto(
    int EmployerId,
    string UserId,
    string FullName,
    string Email,
    string? Position,
    string MembershipStatus,
    bool IsOwner,
    bool IsActive,
    DateTime JoinedAt);

public sealed record AdminJobStatusCountDto(string Status, int Count);

public sealed record AdminCompanyJobDto(
    int Id,
    string Title,
    string Status,
    DateTime CreatedAt,
    int ApplicationCount,
    bool IsRemoved);
