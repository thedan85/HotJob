using hotjob.DTOs.Employer;
using hotjob.Models;
using hotjob.Repositories.Employer;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

public sealed class NotificationService(IApplicantRepository repo) : INotificationService
{
    public async Task<ServiceResult<NotificationResponse>> NotifyAsync(
        string? userId,
        int applicationId,
        SendNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return ServiceResult<NotificationResponse>.Fail(ServiceErrorType.Validation, "Title is required.");
        if (title.Length > 200)
            return ServiceResult<NotificationResponse>.Fail(ServiceErrorType.Validation, "Title must be at most 200 characters.");

        var employerResult = await EmployerGuard.RequireActiveAsync(repo, userId, cancellationToken);
        if (!employerResult.Success)
            return ServiceResult<NotificationResponse>.Fail(employerResult.ErrorType!.Value, employerResult.Errors);

        var app = await repo.GetOwnedForUpdateAsync(
            employerResult.Data!.CompanyProfileId, applicationId, cancellationToken);
        if (app is null)
            return ServiceResult<NotificationResponse>.Fail(ServiceErrorType.NotFound, "Application not found.");

        var notification = new Notification
        {
            UserId = app.Candidate.UserId,
            Title = title,
            Content = string.IsNullOrWhiteSpace(request.Content) ? null : request.Content.Trim(),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        repo.AddNotification(notification);
        await repo.SaveChangesAsync(cancellationToken);

        return ServiceResult<NotificationResponse>.Ok(ApplicantMapping.ToNotificationResponse(notification));
    }
}
