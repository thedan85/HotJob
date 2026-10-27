using System.Globalization;
using hotjob.Data;
using hotjob.DTOs.JobAlerts;
using hotjob.DTOs.ML;
using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.ML;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace hotjob.Services.JobAlerts;

/// <summary>
/// Premium job alerts (contract D11): on publish, score every Premium candidate's resumes
/// and create one in-app notification per candidate whose best resume clears the threshold.
/// </summary>
public sealed class JobAlertService(
    ApplicationDbContext dbContext,
    IMatchScoreService matchScoreService,
    ICandidatePremiumService premiumService,
    IOptions<JobAlertsOptions> options,
    TimeProvider timeProvider,
    ILogger<JobAlertService> logger) : IJobAlertService
{
    private const string TitlePrefix = "New matching job: ";
    private const string UpsellMessage =
        "Job alerts are a Premium feature. Upgrade to be told when a new job matches your CV.";

    public async Task<int> NotifyForPublishedJobAsync(
        int jobPostingId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var job = await dbContext.JobPostings
            .AsNoTracking()
            .Where(row => row.Id == jobPostingId)
            .Select(row => new
            {
                row.Id,
                row.Title,
                row.Status,
                row.ExpiryDate,
                CompanyName = row.Company.CompanyName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            logger.LogWarning("Job alerts skipped: job {JobId} no longer exists.", jobPostingId);
            return 0;
        }

        if (job.Status != JobStatus.Published || (job.ExpiryDate is { } expiry && expiry <= now))
        {
            return 0;
        }

        var premiumUserIds = await dbContext.Candidates
            .AsNoTracking()
            .Where(candidate => candidate.PremiumUntil != null
                && candidate.PremiumUntil > now
                && candidate.User.IsActive)
            .Select(candidate => candidate.UserId)
            .ToListAsync(cancellationToken);

        if (premiumUserIds.Count == 0)
        {
            return 0;
        }

        var resumeIdsByUser = (await dbContext.Resumes
                .AsNoTracking()
                .Where(resume => premiumUserIds.Contains(resume.Candidate.UserId))
                .Select(resume => new { resume.Id, resume.Candidate.UserId })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.UserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Id).ToList(),
                StringComparer.Ordinal);

        var alreadyNotified = (await dbContext.Notifications
                .AsNoTracking()
                .Where(notification => notification.Kind == NotificationKinds.JobAlert
                    && notification.RelatedJobPostingId == jobPostingId)
                .Select(notification => notification.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var minimumScore = options.Value.MinMatchPercent;
        var created = 0;

        foreach (var userId in premiumUserIds)
        {
            if (alreadyNotified.Contains(userId)
                || !resumeIdsByUser.TryGetValue(userId, out var resumeIds)
                || resumeIds.Count == 0)
            {
                continue;
            }

            var best = await BestScoreAsync(userId, job.Id, resumeIds, cancellationToken);
            if (best is null || best.MatchPercentage < minimumScore)
            {
                continue;
            }

            var notification = new Notification
            {
                UserId = userId,
                Kind = NotificationKinds.JobAlert,
                RelatedJobPostingId = job.Id,
                Title = BuildTitle(job.Title),
                Content = BuildContent(job.CompanyName, best.MatchPercentage),
                IsRead = false,
                CreatedAt = now
            };
            dbContext.Notifications.Add(notification);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                created++;
                logger.LogInformation(
                    "Created job alert {NotificationId} for user {UserId} on job {JobId} with a {Score}% match.",
                    notification.Id, userId, job.Id, best.MatchPercentage);
            }
            catch (DbUpdateException exception)
            {
                // ponytail: a concurrent publish can win the unique (UserId, Kind,
                // RelatedJobPostingId) race; keep the other candidates' alerts and move on.
                dbContext.Entry(notification).State = EntityState.Detached;
                logger.LogWarning(
                    exception,
                    "Job alert for user {UserId} and job {JobId} already exists; skipped the duplicate.",
                    userId, job.Id);
            }
        }

        return created;
    }

    public async Task<ServiceResult<JobAlertsPageDto>> GetAlertsAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var status = await premiumService.GetStatusAsync(userId, cancellationToken);
        if (status is null)
        {
            return ServiceResult<JobAlertsPageDto>.Fail(
                ServiceErrorType.NotFound, "Candidate profile not found.");
        }

        if (!status.IsPremium)
        {
            return ServiceResult<JobAlertsPageDto>.Ok(
                new JobAlertsPageDto(false, Array.Empty<JobAlertDto>(), 0, UpsellMessage));
        }

        var alerts = await dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId
                && notification.Kind == NotificationKinds.JobAlert)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Select(notification => new JobAlertDto(
                notification.Id,
                notification.RelatedJobPostingId,
                notification.Title,
                notification.Content,
                notification.IsRead,
                notification.CreatedAt))
            .ToListAsync(cancellationToken);

        return ServiceResult<JobAlertsPageDto>.Ok(
            new JobAlertsPageDto(true, alerts, alerts.Count(alert => !alert.IsRead), null));
    }

    public async Task<ServiceResult<int>> MarkAllReadAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<int>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");
        }

        var updated = await dbContext.Notifications
            .Where(notification => notification.UserId == userId
                && notification.Kind == NotificationKinds.JobAlert
                && !notification.IsRead)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.IsRead, true),
                cancellationToken);

        return ServiceResult<int>.Ok(updated);
    }

    private async Task<MatchScoreDto?> BestScoreAsync(
        string userId,
        int jobId,
        IReadOnlyList<int> resumeIds,
        CancellationToken cancellationToken)
    {
        MatchScoreDto? best = null;
        foreach (var resumeId in resumeIds)
        {
            var result = await matchScoreService.GetAsync(
                userId, jobId, resumeId, cancellationToken);
            if (!result.Success || result.Data is not { Available: true } score)
            {
                continue;
            }

            if (best is null || score.MatchPercentage > best.MatchPercentage)
            {
                best = score;
            }
        }

        return best;
    }

    private static string BuildTitle(string jobTitle)
    {
        const int maxLength = 200;
        var available = maxLength - TitlePrefix.Length;
        var title = jobTitle.Length <= available
            ? jobTitle
            : jobTitle[..(available - 1)] + "…";
        return TitlePrefix + title;
    }

    private static string BuildContent(string companyName, double score) =>
        $"{companyName} · {score.ToString("0", CultureInfo.InvariantCulture)}% match with your best CV";
}
