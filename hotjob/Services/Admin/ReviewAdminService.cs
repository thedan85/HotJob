using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models;
using hotjob.Models.Admin;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

public sealed class ReviewAdminService(
    ApplicationDbContext dbContext,
    IAdminAuditService auditService,
    ILogger<ReviewAdminService> logger) : IReviewAdminService
{
    private const int MaxReasonLength = 500;

    public async Task<ServiceResult<PagedResponse<ReviewListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        bool? isDeleted = null,
        int? companyId = null,
        bool reportedOnly = false,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Reviews.AsNoTracking();

        if (isDeleted.HasValue)
        {
            query = query.Where(r => r.IsDeleted == isDeleted.Value);
        }

        if (companyId.HasValue)
        {
            query = query.Where(r => r.CompanyProfileId == companyId.Value);
        }

        if (reportedOnly)
        {
            query = query.Where(r => r.Reports.Any(report => report.Status == ReviewReportStatus.Pending));
        }

        // Report queue: most-reported first, then the one waiting longest. Otherwise newest first.
        var ordered = reportedOnly
            ? query
                .OrderByDescending(r => r.Reports.Count(report => report.Status == ReviewReportStatus.Pending))
                .ThenBy(r => r.Reports.Where(report => report.Status == ReviewReportStatus.Pending).Min(report => report.CreatedAt))
                .ThenBy(r => r.Id)
            : query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id);

        var total = await query.CountAsync(cancellationToken);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReviewListItemDto(
                r.Id,
                r.CompanyProfileId,
                r.Company.CompanyName,
                r.CandidateId,
                r.Candidate.FullName,
                r.RatingScore,
                r.ReviewText,
                r.CreatedAt,
                r.IsDeleted,
                r.DeletedAt,
                r.DeletedByAdminId,
                r.Reports.Count(report => report.Status == ReviewReportStatus.Pending)))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<ReviewListItemDto>>.Ok(new PagedResponse<ReviewListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<ReviewDetailDto>> GetDetailAsync(
        int reviewId,
        CancellationToken cancellationToken = default)
    {
        var review = await dbContext.Reviews
            .AsNoTracking()
            .Include(r => r.Company)
            .Include(r => r.Candidate).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null)
            return ServiceResult<ReviewDetailDto>.Fail(ServiceErrorType.NotFound, "Review not found.");

        return ServiceResult<ReviewDetailDto>.Ok(new ReviewDetailDto(
            review.Id,
            review.CompanyProfileId,
            review.Company.CompanyName,
            review.CandidateId,
            review.Candidate.User.Email ?? string.Empty,
            review.Candidate.FullName,
            review.RatingScore,
            review.ReviewText,
            review.CreatedAt,
            review.IsDeleted,
            review.DeletedAt,
            review.DeletedByAdminId,
            review.DeletionReason));
    }

    public async Task<ServiceResult> DeleteAsync(
        int reviewId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed?.Length > MaxReasonLength)
            return ServiceResult.Fail(ServiceErrorType.Validation, $"The reason must be at most {MaxReasonLength} characters.");

        var review = await dbContext.Reviews
            .Include(r => r.Company)
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Review not found.");
        if (review.IsDeleted)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Review is already hidden.");

        // Soft delete: the row stays for the audit trail and stops counting towards public ratings.
        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            var now = DateTime.UtcNow;
            review.IsDeleted = true;
            review.DeletedAt = now;
            review.DeletedByAdminId = adminUserId;
            review.DeletionReason = trimmed;

            // Hiding the review is the decision on every report still waiting on it.
            var pending = await dbContext.ReviewReports
                .Where(report => report.ReviewId == reviewId && report.Status == ReviewReportStatus.Pending)
                .ToListAsync(cancellationToken);
            foreach (var report in pending)
            {
                Resolve(report, ReviewReportStatus.Upheld, adminUserId, trimmed, now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.ReviewHide,
                AuditTargetTypes.Review,
                review.Id.ToString(),
                trimmed is null
                    ? $"Hid a {review.RatingScore}-star review of '{review.Company.CompanyName}'."
                    : $"Hid a {review.RatingScore}-star review of '{review.Company.CompanyName}': {trimmed}");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} removed/hidden review {ReviewId}.", adminUserId, reviewId);
        return ServiceResult.Ok();
    }
    public async Task<ServiceResult<IReadOnlyList<ReviewReportDto>>> GetReportsAsync(
        int reviewId,
        CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Reviews.AsNoTracking().AnyAsync(r => r.Id == reviewId, cancellationToken))
            return ServiceResult<IReadOnlyList<ReviewReportDto>>.Fail(ServiceErrorType.NotFound, "Review not found.");

        var reports = await dbContext.ReviewReports
            .AsNoTracking()
            .Where(report => report.ReviewId == reviewId)
            .OrderByDescending(report => report.CreatedAt)
            .ThenByDescending(report => report.Id)
            .Select(report => new ReviewReportDto(
                report.Id,
                report.ReviewId,
                report.Reporter.Email ?? string.Empty,
                report.Reason.ToString(),
                report.Details,
                report.Status.ToString(),
                report.CreatedAt,
                report.ResolvedAt,
                report.ResolutionNote))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<ReviewReportDto>>.Ok(reports);
    }

    public async Task<ServiceResult> DismissReportsAsync(
        int reviewId,
        string adminUserId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > MaxReasonLength)
            return ServiceResult.Fail(ServiceErrorType.Validation, $"The note must be at most {MaxReasonLength} characters.");

        var review = await dbContext.Reviews
            .Include(r => r.Company)
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
        if (review is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Review not found.");

        var pending = await dbContext.ReviewReports
            .Where(report => report.ReviewId == reviewId && report.Status == ReviewReportStatus.Pending)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "This review has no pending reports.");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            var now = DateTime.UtcNow;
            foreach (var report in pending)
            {
                Resolve(report, ReviewReportStatus.Dismissed, adminUserId, trimmed, now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.LogAsync(
                AuditActions.ReviewReportsDismiss,
                AuditTargetTypes.Review,
                review.Id.ToString(),
                $"Dismissed {pending.Count} report(s) on a review of '{review.Company.CompanyName}'"
                    + (trimmed is null ? "." : $": {trimmed}"));
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} dismissed {Count} report(s) on review {ReviewId}.", adminUserId, pending.Count, reviewId);
        return ServiceResult.Ok();
    }

    private static void Resolve(ReviewReport report, ReviewReportStatus outcome, string adminUserId, string? note, DateTime now)
    {
        report.Status = outcome;
        report.ResolvedAt = now;
        report.ResolvedByAdminId = adminUserId;
        report.ResolutionNote = note;
    }
}
