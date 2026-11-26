using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;

namespace hotjob.Services.Job;

/// <summary>Shared side effects of taking a job off the market (employer close or admin removal).</summary>
internal static class JobClosure
{
    public static readonly ApplicationStatus[] OpenStatuses =
    [
        ApplicationStatus.Pending,
        ApplicationStatus.Viewed,
        ApplicationStatus.Shortlisted,
        ApplicationStatus.Interviewing
    ];

    /// <summary>
    /// Rejects every open application, records history with <paramref name="historyNote"/> and
    /// notifies each affected candidate. Requires <c>job.Applications</c> with <c>Candidate</c> loaded.
    /// Changes are only staged; the caller saves.
    /// </summary>
    public static int RejectOpenApplications(
        ApplicationDbContext dbContext,
        JobPosting job,
        string? actorUserId,
        string historyNote,
        DateTime now)
    {
        var rejected = 0;
        foreach (var application in job.Applications.Where(application => OpenStatuses.Contains(application.CurrentStatus)))
        {
            application.CurrentStatus = ApplicationStatus.Rejected;
            dbContext.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.Id,
                Status = ApplicationStatus.Rejected,
                ChangedByUserId = actorUserId,
                Notes = historyNote,
                ChangedAt = now
            });
            dbContext.Notifications.Add(new Notification
            {
                UserId = application.Candidate.UserId,
                Title = "Application closed",
                Content = $"The job \"{job.Title}\" is no longer open, so your application was closed.",
                RelatedJobPostingId = job.Id,
                IsRead = false,
                CreatedAt = now
            });
            rejected++;
        }

        return rejected;
    }
}
