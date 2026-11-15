using hotjob.DTOs.Candidate;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

/// <summary>
/// Routes notification reads/writes through the per-circuit <see cref="CircuitDbGate"/>
/// so they never overlap another component's query on the shared scoped DbContext.
/// </summary>
public sealed class NotificationFeed(ICandidateService inner, CircuitDbGate gate)
{
    public Task<ServiceResult<IReadOnlyList<MyNotificationDto>>> ListAsync(
        string? userId, CancellationToken cancellationToken = default) =>
        gate.RunAsync(() => inner.GetMyNotificationsAsync(userId, cancellationToken));

    public Task<ServiceResult<MyNotificationDto>> MarkReadAsync(
        string? userId, int notificationId, CancellationToken cancellationToken = default) =>
        gate.RunAsync(() => inner.MarkNotificationReadAsync(userId, notificationId, cancellationToken));

    public Task<ServiceResult<int>> MarkAllReadAsync(
        string? userId, CancellationToken cancellationToken = default) =>
        gate.RunAsync(() => inner.MarkAllNotificationsReadAsync(userId, cancellationToken));
}
