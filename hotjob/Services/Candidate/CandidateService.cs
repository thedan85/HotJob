using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
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
        var candidate = await ResolveCandidateIdAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<IReadOnlyList<MyApplicationDto>>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var applications = await OwnedApplications(candidate.Data)
            .Include(application => application.JobPosting).ThenInclude(job => job.Company)
            .Include(application => application.Resume)
            .Include(application => application.StatusHistory)
            .Include(application => application.Interviews)
            .OrderByDescending(application => application.AppliedAt)
            .ThenByDescending(application => application.Id)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<MyApplicationDto>>.Ok(
            applications.Select(ToDto).ToArray());
    }

    public async Task<ServiceResult<MyApplicationsPageDto>> GetMyApplicationsPageAsync(
        string? userId,
        MyApplicationsQuery query,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateIdAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<MyApplicationsPageDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(
            query.PageSize <= 0 ? MyApplicationsQuery.DefaultPageSize : query.PageSize,
            1,
            MyApplicationsQuery.MaxPageSize);

        var all = OwnedApplications(candidate.Data);
        var totalApplications = await all.CountAsync(cancellationToken);
        var inProgress = await all.CountAsync(
            application => application.CurrentStatus == ApplicationStatus.Pending
                || application.CurrentStatus == ApplicationStatus.Viewed
                || application.CurrentStatus == ApplicationStatus.Shortlisted
                || application.CurrentStatus == ApplicationStatus.Interviewing,
            cancellationToken);
        var offers = await all.CountAsync(
            application => application.CurrentStatus == ApplicationStatus.Offered, cancellationToken);

        var filtered = ApplyFilters(all, query);
        var total = await filtered.CountAsync(cancellationToken);

        var offset = (long)(page - 1) * pageSize;
        var items = offset >= total
            ? Array.Empty<MyApplicationDto>()
            : (await filtered
                .Include(application => application.JobPosting).ThenInclude(job => job.Company)
                .Include(application => application.Resume)
                .Include(application => application.StatusHistory)
                .Include(application => application.Interviews)
                .OrderByDescending(application => application.AppliedAt)
                .ThenByDescending(application => application.Id)
                .Skip((int)offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken))
              .Select(ToDto)
              .ToArray();

        return ServiceResult<MyApplicationsPageDto>.Ok(new MyApplicationsPageDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            TotalApplications = totalApplications,
            InProgressCount = inProgress,
            OfferCount = offers
        });
    }

    public async Task<ServiceResult<PagedResponse<MyNotificationDto>>> GetMyNotificationsAsync(
        string? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<PagedResponse<MyNotificationDto>>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Job alerts have their own page; this feed carries employer messages only.
        // The null branch is explicit because SQL would reject NULL != 'JobAlert'.
        var query = dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId
                && (notification.Kind == null || notification.Kind != NotificationKinds.JobAlert));

        var total = await query.CountAsync(cancellationToken);
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
        {
            return ServiceResult<PagedResponse<MyNotificationDto>>.Ok(
                new PagedResponse<MyNotificationDto> { TotalCount = total, Page = page, PageSize = pageSize });
        }

        var notifications = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<MyNotificationDto>>.Ok(new PagedResponse<MyNotificationDto>
        {
            Items = notifications.Select(ToDto).ToArray(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<int>> GetUnreadNotificationCountAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<int>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        var unread = await dbContext.Notifications
            .AsNoTracking()
            .CountAsync(
                notification => notification.UserId == userId
                    && !notification.IsRead
                    && (notification.Kind == null || notification.Kind != NotificationKinds.JobAlert),
                cancellationToken);

        return ServiceResult<int>.Ok(unread);
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

    public async Task<ServiceResult<int>> MarkAllNotificationsReadAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<int>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        var updated = await dbContext.Notifications
            .Where(notification => notification.UserId == userId
                && !notification.IsRead
                && (notification.Kind == null || notification.Kind != NotificationKinds.JobAlert))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.IsRead, true),
                cancellationToken);

        return ServiceResult<int>.Ok(updated);
    }

    private IQueryable<Application> OwnedApplications(int candidateId) =>
        dbContext.Applications
            .AsNoTracking()
            .Where(application => application.CandidateId == candidateId);

    private static IQueryable<Application> ApplyFilters(
        IQueryable<Application> query, MyApplicationsQuery filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(application =>
                EF.Functions.Like(application.JobPosting.Title, pattern)
                || EF.Functions.Like(application.JobPosting.Company.CompanyName, pattern));
        }

        return filter.StatusGroup?.Trim().ToLowerInvariant() switch
        {
            "active" => query.Where(application =>
                application.CurrentStatus == ApplicationStatus.Pending
                || application.CurrentStatus == ApplicationStatus.Viewed
                || application.CurrentStatus == ApplicationStatus.Shortlisted
                || application.CurrentStatus == ApplicationStatus.Interviewing),
            "offers" => query.Where(application => application.CurrentStatus == ApplicationStatus.Offered),
            "closed" => query.Where(application =>
                application.CurrentStatus == ApplicationStatus.Rejected
                || application.CurrentStatus == ApplicationStatus.Withdrawn),
            _ => query
        };
    }

    private async Task<ServiceResult<int>> ResolveCandidateIdAsync(
        string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<int>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        var candidateId = await dbContext.Candidates
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => (int?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return candidateId is null
            ? ServiceResult<int>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.")
            : ServiceResult<int>.Ok(candidateId.Value);
    }

    private static MyApplicationDto ToDto(Application application) =>
        new(
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
                .ToArray(),
            application.Interviews
                .OrderBy(interview => interview.InterviewDate)
                .Select(interview => new InterviewDto(
                    interview.Id,
                    interview.ApplicationId,
                    interview.Round,
                    interview.InterviewDate,
                    interview.LocationOrLink,
                    interview.InterviewType,
                    interview.Notes,
                    interview.Status))
                .ToArray());

    private static MyNotificationDto ToDto(Notification notification) =>
        new(notification.Id, notification.Title, notification.Content, notification.IsRead, notification.CreatedAt);
}
