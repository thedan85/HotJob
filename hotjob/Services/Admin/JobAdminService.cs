using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models;
using hotjob.Models.Admin;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Services.Common;
using hotjob.Services.Job;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

public sealed class JobAdminService(
    ApplicationDbContext dbContext,
    IAdminAuditService auditService,
    ILogger<JobAdminService> logger,
    TimeProvider? timeProvider = null) : IJobAdminService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private const int MaxReasonLength = 500;

    public Task<ServiceResult<PagedResponse<JobListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        JobStatus? status = null,
        CancellationToken cancellationToken = default) =>
        ListAsync(new AdminJobQuery { Page = page, PageSize = pageSize, Search = search, Status = status }, cancellationToken);

    public async Task<ServiceResult<PagedResponse<JobListItemDto>>> ListAsync(
        AdminJobQuery filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var search = filter.Search;
        var status = filter.Status;

        var query = dbContext.JobPostings.AsNoTracking();

        if (filter.CompanyId is { } companyId)
        {
            query = query.Where(j => j.CompanyProfileId == companyId);
        }

        if (filter.IsFeatured is { } featured)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            query = featured
                ? query.Where(j => j.FeaturedUntil != null && j.FeaturedUntil > now)
                : query.Where(j => j.FeaturedUntil == null || j.FeaturedUntil <= now);
        }

        if (filter.IsModerated is { } moderated)
        {
            query = moderated
                ? query.Where(j => j.ModeratedAt != null)
                : query.Where(j => j.ModeratedAt == null);
        }

        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(j =>
                EF.Functions.Like(j.Title, term) ||
                EF.Functions.Like(j.Company.CompanyName, term));
        }

        var ordered = status == JobStatus.PendingApproval
            // Review queue: oldest submission first, so nothing waits indefinitely.
            ? query.OrderBy(j => j.SubmittedForApprovalAt ?? j.CreatedAt).ThenBy(j => j.Id)
            : query.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id);

        var total = await query.CountAsync(cancellationToken);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobListItemDto(
                j.Id,
                j.Title,
                j.Company.CompanyName,
                j.Status.ToString(),
                j.Company.Location,
                j.ExpiryDate,
                j.CreatedAt,
                j.SubmittedForApprovalAt,
                j.Applications.Count,
                j.ModeratedAt,
                j.ModerationReason,
                j.CompanyProfileId,
                j.FeaturedUntil))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<JobListItemDto>>.Ok(new PagedResponse<JobListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public Task<ServiceResult<PagedResponse<JobListItemDto>>> ListPendingAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default) =>
        ListAsync(page, pageSize, search, JobStatus.PendingApproval, cancellationToken);

    public async Task<ServiceResult<JobDetailDto>> GetDetailAsync(
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.JobPostings
            .AsNoTracking()
            .Include(j => j.Company)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
            return ServiceResult<JobDetailDto>.Fail(ServiceErrorType.NotFound, "Job posting not found.");

        // ReviewedAt holds the latest decision; expose it under the matching outcome only.
        return ServiceResult<JobDetailDto>.Ok(new JobDetailDto(
            job.Id,
            job.Title,
            job.Description,
            job.Company.CompanyName,
            job.CompanyProfileId,
            job.Status.ToString(),
            job.Company.Location,
            job.CreatedAt,
            job.ExpiryDate,
            job.SubmittedForApprovalAt,
            job.Status is JobStatus.Approved or JobStatus.Published ? job.ReviewedAt : null,
            job.Status == JobStatus.Rejected ? job.ReviewedAt : null,
            job.RejectionReason,
            job.ReviewedByAdminId,
            job.ModeratedAt,
            job.ModerationReason));
    }

    public async Task<ServiceResult> ApproveAsync(
        int jobId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var job = await FindAsync(jobId, cancellationToken);
        if (job is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Job posting not found.");
        if (job.Status != JobStatus.PendingApproval)
            return ServiceResult.Fail(ServiceErrorType.Conflict, $"Only jobs pending approval can be approved (current status: {job.Status}).");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            job.Status = JobStatus.Approved;
            job.ReviewedAt = DateTime.UtcNow;
            job.ReviewedByAdminId = adminUserId;
            job.RejectionReason = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.JobApprove,
                AuditTargetTypes.Job,
                job.Id.ToString(),
                $"Approved job '{job.Title}' at '{job.Company.CompanyName}'.");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} approved job posting {JobId}.", adminUserId, jobId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectAsync(
        int jobId,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ServiceResult.Fail(ServiceErrorType.Validation, "A rejection reason is required.");
        if (trimmed.Length > MaxReasonLength)
            return ServiceResult.Fail(ServiceErrorType.Validation, $"The reason must be at most {MaxReasonLength} characters.");

        var job = await FindAsync(jobId, cancellationToken);
        if (job is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Job posting not found.");
        if (job.Status != JobStatus.PendingApproval)
            return ServiceResult.Fail(ServiceErrorType.Conflict, $"Only jobs pending approval can be rejected (current status: {job.Status}).");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            job.Status = JobStatus.Rejected;
            job.ReviewedAt = DateTime.UtcNow;
            job.ReviewedByAdminId = adminUserId;
            job.RejectionReason = trimmed;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.JobReject,
                AuditTargetTypes.Job,
                job.Id.ToString(),
                $"Rejected job '{job.Title}' at '{job.Company.CompanyName}': {trimmed}");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} rejected job posting {JobId}. Reason: {Reason}", adminUserId, jobId, trimmed);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<int>> RemoveAsync(
        int jobId,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ServiceResult<int>.Fail(ServiceErrorType.Validation, "A removal reason is required.");
        if (trimmed.Length > MaxReasonLength)
            return ServiceResult<int>.Fail(ServiceErrorType.Validation, $"The reason must be at most {MaxReasonLength} characters.");

        var job = await dbContext.JobPostings
            .Include(j => j.Company)
            .Include(j => j.Applications)
            .ThenInclude(application => application.Candidate)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
            return ServiceResult<int>.Fail(ServiceErrorType.NotFound, "Job posting not found.");
        if (job.ModeratedAt is not null)
            return ServiceResult<int>.Fail(ServiceErrorType.Conflict, "This job has already been removed.");

        var rejected = 0;
        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            job.Status = JobStatus.Closed;
            job.ModeratedAt = now;
            job.ModeratedByAdminId = adminUserId;
            job.ModerationReason = trimmed;
            rejected = JobClosure.RejectOpenApplications(
                dbContext, job, adminUserId, ApplicationHistoryNotes.JobRemovedByAdmin, now);
            await CompanyMemberNotifier.StageForActiveMembersAsync(
                dbContext,
                job.CompanyProfileId,
                NotificationKinds.JobModeration,
                "Job removed by admin",
                $"\"{job.Title}\" was removed by an administrator and can no longer be reopened. Reason: {trimmed}",
                job.Id,
                now,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.JobRemove,
                AuditTargetTypes.Job,
                job.Id.ToString(),
                $"Removed job '{job.Title}' at '{job.Company.CompanyName}' ({rejected} open application(s) closed): {trimmed}");
        }, cancellationToken);

        logger.LogInformation(
            "Admin {AdminId} removed job posting {JobId}; {Rejected} open applications closed. Reason: {Reason}",
            adminUserId, jobId, rejected, trimmed);
        return ServiceResult<int>.Ok(rejected);
    }

    private Task<JobPosting?> FindAsync(int jobId, CancellationToken cancellationToken) =>
        dbContext.JobPostings
            .Include(j => j.Company)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
}
