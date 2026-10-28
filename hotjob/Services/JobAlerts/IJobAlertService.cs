using hotjob.DTOs.JobAlerts;
using hotjob.Services.Common;

namespace hotjob.Services.JobAlerts;

/// <summary>Generates and reads in-app job alerts (contract D11). Free for all candidates.</summary>
public interface IJobAlertService
{
    /// <summary>
    /// Creates one alert per eligible candidate when a job is published. Only a
    /// currently Published, unexpired job is considered. Returns the number of alerts
    /// created; calling it again for the same job never duplicates an existing alert.
    /// </summary>
    Task<int> NotifyForPublishedJobAsync(
        int jobPostingId,
        CancellationToken cancellationToken = default);

    /// <summary>Newest-first alerts for the caller.</summary>
    Task<ServiceResult<JobAlertsPageDto>> GetAlertsAsync(
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks only the caller's own job-alert notifications read.</summary>
    Task<ServiceResult<int>> MarkAllReadAsync(
        string? userId,
        CancellationToken cancellationToken = default);
}
