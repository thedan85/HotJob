using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Job;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

/// <summary>Admin review of job postings that the company has submitted for approval.</summary>
public sealed class JobAdminService(
    ApplicationDbContext dbContext,
    ILogger<JobAdminService> logger) : IJobAdminService
{
    public async Task<ServiceResult<PagedResponse<JobListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.JobPostings
            .AsNoTracking()
            .Include(j => j.Company)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(j =>
                EF.Functions.Like(j.Title, term) ||
                EF.Functions.Like(j.Company.CompanyName, term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobListItemDto(
                j.Id,
                j.Title,
                j.Company.CompanyName,
                j.Status.ToString(),
                j.Company.Location,
                j.ExpiryDate))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<JobListItemDto>>.Ok(new PagedResponse<JobListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<PagedResponse<JobListItemDto>>> ListPendingAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.JobPostings
            .AsNoTracking()
            .Include(j => j.Company)
            .Where(j => j.Status == JobStatus.PendingApproval);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(j =>
                EF.Functions.Like(j.Title, term) ||
                EF.Functions.Like(j.Company.CompanyName, term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(j => j.SubmittedForApprovalAt ?? j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobListItemDto(
                j.Id,
                j.Title,
                j.Company.CompanyName,
                j.Status.ToString(),
                j.Company.Location,
                j.ExpiryDate))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<JobListItemDto>>.Ok(new PagedResponse<JobListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

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
            IsApprovedOrLater(job.Status) ? job.ReviewedAt : null,
            job.Status == JobStatus.Rejected ? job.ReviewedAt : null,
            job.ReviewedByAdminId,
            job.RejectionReason,
            job.CompanyReviewedAt));
    }

    public async Task<ServiceResult> ApproveAsync(
        int jobId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.JobPostings
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Job posting not found.");

        if (EnsureAwaitingAdminReview(job) is { } conflict)
            return conflict;

        job.Status = JobStatus.Approved;
        job.ReviewedAt = DateTime.UtcNow;
        job.ReviewedByAdminId = adminUserId;
        job.RejectionReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} approved job posting {JobId}.", adminUserId, jobId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectAsync(
        int jobId,
        string adminUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var normalized = ModerationReason.Normalize(reason);
        if (ModerationReason.ValidateRequired(normalized) is { } invalid)
            return invalid;

        var job = await dbContext.JobPostings
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Job posting not found.");

        if (EnsureAwaitingAdminReview(job) is { } conflict)
            return conflict;

        job.Status = JobStatus.Rejected;
        job.ReviewedAt = DateTime.UtcNow;
        job.ReviewedByAdminId = adminUserId;
        job.RejectionReason = normalized;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} rejected job posting {JobId}. Reason: {Reason}", adminUserId, jobId, normalized);
        return ServiceResult.Ok();
    }

    /// <summary>Admin may only act on jobs in <see cref="JobStatus.PendingApproval"/>; returns a Conflict otherwise.</summary>
    private static ServiceResult? EnsureAwaitingAdminReview(JobPosting job) => job.Status switch
    {
        JobStatus.PendingApproval => null,
        JobStatus.PendingCompanyApproval => ServiceResult.Fail(
            ServiceErrorType.Conflict,
            "Job posting is awaiting approval by the company owner and cannot be reviewed by an admin yet."),
        _ => ServiceResult.Fail(
            ServiceErrorType.Conflict,
            $"Only job postings pending approval can be reviewed (current status: {job.Status}).")
    };

    private static bool IsApprovedOrLater(JobStatus status) =>
        status is JobStatus.Approved or JobStatus.Published or JobStatus.Closed or JobStatus.Expired;
}
