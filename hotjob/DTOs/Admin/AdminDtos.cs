using hotjob.Models.Job;
using hotjob.Models.Users;

namespace hotjob.DTOs.Admin;

public sealed record AdminDashboardDto(
    int TotalUsers,
    int ActiveUsers,
    int AdminUsers,
    int CandidateUsers,
    int EmployerUsers,
    int TotalCompanies,
    int PendingCompanies,
    int ApprovedCompanies,
    int RejectedCompanies,
    int TotalJobs,
    int PublishedJobs,
    int DraftJobs,
    int ClosedJobs,
    int ExpiredJobs,
    int TotalApplications = 0,
    int ApplicationsLast30Days = 0,
    IReadOnlyList<AdminStatusCountDto>? ApplicationsByStatus = null,
    decimal RevenueThisMonth = 0m,
    int PaidPaymentsThisMonth = 0);

public sealed record AdminStatusCountDto(string Status, int Count);

public sealed record AdminUserListItemDto(
    string UserId,
    string Email,
    string? FullName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt,
    string AccountType);

public sealed record AdminTrainingLogDto(
    int Id,
    int RecordCount,
    double AccuracyScore,
    string? ModelFilePath,
    string? TrainedByEmail,
    DateTime TrainedAt,
    bool IsActive);

public sealed record AdminAuditLogDto(
    int Id,
    DateTime CreatedAt,
    string AdminUserId,
    string AdminEmail,
    string Action,
    string TargetType,
    string TargetId,
    string? Detail);

/// <summary>Filters for the admin account list. Every field is optional.</summary>
public sealed class AdminUserQuery
{
    /// <summary>Matches email or full name (case-insensitive, substring).</summary>
    public string? Search { get; init; }

    /// <summary>Admin, Candidate or Employer.</summary>
    public string? Role { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record AdminUserDetailDto(
    string UserId,
    string Email,
    string? FullName,
    bool IsActive,
    bool EmailConfirmed,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles,
    AdminCandidateSummaryDto? Candidate,
    AdminEmployerSummaryDto? Employer,
    IReadOnlyList<AdminAuditLogDto> RecentAudit);

public sealed record AdminCandidateSummaryDto(
    int CandidateId,
    string FullName,
    string? Position,
    string? PreferredLocation,
    string? JobSeekingStatus,
    bool IsPremium,
    DateTime? PremiumUntil,
    bool IsDiscoverable,
    IReadOnlyList<AdminResumeSummaryDto> Resumes,
    int ApplicationCount,
    IReadOnlyList<AdminUserApplicationDto> RecentApplications);

public sealed record AdminResumeSummaryDto(
    int Id,
    string Title,
    bool IsPrimary,
    bool HasFile,
    DateTime CreatedAt);

public sealed record AdminUserApplicationDto(
    int Id,
    int JobId,
    string JobTitle,
    string CompanyName,
    hotjob.Models.Application.ApplicationStatus Status,
    DateTime AppliedAt);

public sealed record AdminEmployerSummaryDto(
    int EmployerId,
    string FullName,
    string? Position,
    int CompanyId,
    string CompanyName,
    hotjob.Models.Users.MembershipStatus MembershipStatus,
    bool IsOwner,
    bool IsActive,
    int JobsCreated);

/// <summary>Returned once by a password reset; never stored or logged.</summary>
public sealed record AdminPasswordResetDto(string TemporaryPassword);

/// <summary>Monthly admin report over a closed-open UTC range [FromUtc, ToUtc).</summary>
public sealed record AdminReportDto(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<AdminMonthlyReportRow> Months,
    IReadOnlyList<AdminTopItemDto> TopCompanies,
    IReadOnlyList<AdminTopItemDto> TopJobs,
    int TotalApplications,
    decimal TotalRevenue);

/// <summary>One calendar month (UTC). Months without data are present with zeros.</summary>
public sealed record AdminMonthlyReportRow(
    int Year,
    int Month,
    int Applications,
    int NewCandidates,
    int NewEmployers,
    int NewJobs,
    int PaidPayments,
    decimal Revenue,
    IReadOnlyDictionary<string, decimal> RevenueByPurpose);

/// <summary>A ranked company or job; Subtitle is the company name for jobs.</summary>
public sealed record AdminTopItemDto(int Id, string Name, string? Subtitle, int Count);
