using hotjob.DTOs.Candidate;
using hotjob.DTOs.Job;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

/// <summary>
/// Routes notification reads/writes through the per-circuit <see cref="CircuitDbGate"/>
/// so they never overlap another component's query on the shared scoped DbContext.
/// </summary>
public sealed class NotificationFeed(ICandidateService inner, CircuitDbGate gate)
{
    public Task<ServiceResult<PagedResponse<MyNotificationDto>>> ListAsync(
        string? userId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        gate.RunAsync(() => inner.GetMyNotificationsAsync(userId, page, pageSize, cancellationToken));

    public Task<ServiceResult<int>> UnreadCountAsync(
        string? userId, CancellationToken cancellationToken = default) =>
        gate.RunAsync(() => inner.GetUnreadNotificationCountAsync(userId, cancellationToken));

    public Task<ServiceResult<MyNotificationDto>> MarkReadAsync(
        string? userId, int notificationId, CancellationToken cancellationToken = default) =>
        gate.RunAsync(() => inner.MarkNotificationReadAsync(userId, notificationId, cancellationToken));

    public Task<ServiceResult<int>> MarkAllReadAsync(
        string? userId, CancellationToken cancellationToken = default) =>
        gate.RunAsync(() => inner.MarkAllNotificationsReadAsync(userId, cancellationToken));
}
