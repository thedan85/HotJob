using hotjob.DTOs.Employer;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

public interface INotificationService
{
    Task<ServiceResult<NotificationResponse>> NotifyAsync(
        string? userId,
        int applicationId,
        SendNotificationRequest request,
        CancellationToken cancellationToken = default);
}
