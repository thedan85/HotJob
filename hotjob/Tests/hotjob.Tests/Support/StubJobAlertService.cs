using hotjob.DTOs.JobAlerts;
using hotjob.Services.Common;
using hotjob.Services.JobAlerts;

namespace hotjob.Tests.Support;

/// <summary>
/// Records publish notifications and can fail on demand, so job-publishing tests do not
/// need the real ML stack. The alert service itself is tested separately.
/// </summary>
public sealed class StubJobAlertService : IJobAlertService
{
    public List<int> PublishedJobIds { get; } = new();

    public Exception? PublishFailure { get; set; }

    public Task<int> NotifyForPublishedJobAsync(
        int jobPostingId,
        CancellationToken cancellationToken = default)
    {
        PublishedJobIds.Add(jobPostingId);
        return PublishFailure is null
            ? Task.FromResult(1)
            : Task.FromException<int>(PublishFailure);
    }

    public Task<ServiceResult<JobAlertsPageDto>> GetAlertsAsync(
        string? userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<JobAlertsPageDto>.Ok(
            new JobAlertsPageDto(Array.Empty<JobAlertDto>(), 0)));

    public Task<ServiceResult<int>> MarkAllReadAsync(
        string? userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<int>.Ok(0));
}
