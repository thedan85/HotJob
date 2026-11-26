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
