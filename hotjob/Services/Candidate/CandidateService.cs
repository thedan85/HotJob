using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.DTOs.Employer;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Candidate;

/// <summary>
/// Candidate-scoped reads over <see cref="ApplicationDbContext"/> (the same
/// no-repository shape as AccountService). Every query filters by the caller's
/// own user id so one candidate can never read another's rows.
/// </summary>
public sealed class CandidateService(ApplicationDbContext dbContext) : ICandidateService
{
    public async Task<ServiceResult<IReadOnlyList<MyApplicationDto>>> GetMyApplicationsAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<IReadOnlyList<MyApplicationDto>>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        var candidateId = await dbContext.Candidates
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => (int?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (candidateId is null)
            return ServiceResult<IReadOnlyList<MyApplicationDto>>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.");

        var applications = await dbContext.Applications
            .AsNoTracking()
            .Where(application => application.CandidateId == candidateId.Value)
            .Include(application => application.JobPosting).ThenInclude(job => job.Company)
            .Include(application => application.Resume)
            .Include(application => application.StatusHistory)
            .OrderByDescending(application => application.AppliedAt)
            .ThenByDescending(application => application.Id)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<MyApplicationDto>>.Ok(applications
            .Select(application => new MyApplicationDto(
                application.Id,
                application.JobPosting.Title,
                application.JobPosting.Company.CompanyName,
                application.Resume.Title,
                application.CurrentStatus,
                application.AppliedAt,
                application.StatusHistory
                    .OrderBy(history => history.ChangedAt)
                    .Select(history => new HistoryDto(
                        history.Id, history.Status, history.ChangedByUserId, history.Notes, history.ChangedAt))
                    .ToArray()))
            .ToArray());
    }

    public async Task<ServiceResult<IReadOnlyList<MyNotificationDto>>> GetMyNotificationsAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<IReadOnlyList<MyNotificationDto>>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        // Job alerts have their own page; this feed carries employer messages only.
        // The null branch is explicit because SQL would reject NULL != 'JobAlert'.
        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId
                && (notification.Kind == null || notification.Kind != NotificationKinds.JobAlert))
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<MyNotificationDto>>.Ok(notifications.Select(ToDto).ToArray());
    }

    public async Task<ServiceResult<MyNotificationDto>> MarkNotificationReadAsync(
        string? userId,
        int notificationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<MyNotificationDto>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(
                row => row.Id == notificationId && row.UserId == userId,
                cancellationToken);
        if (notification is null)
            return ServiceResult<MyNotificationDto>.Fail(ServiceErrorType.NotFound, "Notification not found.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<MyNotificationDto>.Ok(ToDto(notification));
    }

    private static MyNotificationDto ToDto(Notification notification) =>
        new(notification.Id, notification.Title, notification.Content, notification.IsRead, notification.CreatedAt);
}
