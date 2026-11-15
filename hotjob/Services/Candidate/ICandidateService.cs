using hotjob.DTOs.Candidate;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

/// <summary>Read-side of a candidate's own job applications and notifications.</summary>
public interface ICandidateService
{
    Task<ServiceResult<IReadOnlyList<MyApplicationDto>>> GetMyApplicationsAsync(
        string? userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<MyNotificationDto>>> GetMyNotificationsAsync(
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
