using hotjob.Data;
using hotjob.Models.Job;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Job;

/// <summary>
/// Flips published postings past their deadline to <see cref="JobStatus.Expired"/>.
/// Runs once at startup, then on a timer. A separate scope keeps its DbContext
/// off the Blazor circuits.
/// </summary>
public sealed class JobExpiryService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<JobExpiryService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var expired = await ExpireAsync(dbContext, now, stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("Expired {Count} job postings.", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Job expiry sweep failed.");
            }

            try
            {
                await Task.Delay(Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Marks published postings whose deadline has passed as expired.</summary>
    public static Task<int> ExpireAsync(
        ApplicationDbContext dbContext,
        DateTime now,
        CancellationToken cancellationToken = default) =>
        dbContext.JobPostings
            .Where(job => job.Status == JobStatus.Published
                && job.ExpiryDate != null
                && job.ExpiryDate <= now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(job => job.Status, JobStatus.Expired),
                cancellationToken);
}
