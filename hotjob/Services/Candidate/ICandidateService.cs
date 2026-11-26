using hotjob.DTOs.Candidate;
using hotjob.DTOs.Job;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

/// <summary>Read-side of a candidate's own job applications and notifications.</summary>
public interface ICandidateService
{
    Task<ServiceResult<IReadOnlyList<MyApplicationDto>>> GetMyApplicationsAsync(
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>One database-side page of the caller's applications, filtered by search/status.</summary>
    Task<ServiceResult<MyApplicationsPageDto>> GetMyApplicationsPageAsync(
        string? userId,
        MyApplicationsQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<MyNotificationDto>>> GetMyNotificationsAsync(
        string? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Unread employer messages for the caller, across every page.</summary>
    Task<ServiceResult<int>> GetUnreadNotificationCountAsync(
        string? userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MyNotificationDto>> MarkNotificationReadAsync(
        string? userId,
        int notificationId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks every unread employer message for the caller as read. Returns the number marked.</summary>
    Task<ServiceResult<int>> MarkAllNotificationsReadAsync(
        string? userId,
        CancellationToken cancellationToken = default);
}
