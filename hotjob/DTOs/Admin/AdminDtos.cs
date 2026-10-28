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
    int ExpiredJobs);

public sealed record AdminUserListItemDto(
    string UserId,
    string Email,
    string? FullName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt,
    string AccountType);

public sealed record AdminCompanyListItemDto(
    int Id,
    string CompanyName,
    CompanyStatus Status,
    string? Industry,
    string? Location,
    DateTime? VerifiedAt,
    string? VerifiedByAdminEmail,
    int EmployerCount);

public sealed record AdminJobListItemDto(
    int Id,
    string Title,
    string CompanyName,
    string? CompanyLocation,
    JobStatus Status,
    DateTime CreatedAt,
    int ApplicationCount);
