using System.Linq.Expressions;
using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

/// <summary>Admin moderation of candidate reviews (list, inspect, hide, restore).</summary>
public sealed class ReviewAdminService(
    ApplicationDbContext dbContext,
    ILogger<ReviewAdminService> logger) : IReviewAdminService
{
    /// <summary>SQL-translatable projection shared with <see cref="CompanyAdminService"/>.</summary>
    internal static readonly Expression<Func<Review, ReviewListItemDto>> ToListItem = r => new ReviewListItemDto(
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
        r.DeletionReason);

    public async Task<ServiceResult<PagedResponse<ReviewListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        int? companyId = null,
        bool? isDeleted = null,
        int? rating = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (rating is < 1 or > 5)
            return ServiceResult<PagedResponse<ReviewListItemDto>>.Fail(
                ServiceErrorType.Validation, "Rating must be between 1 and 5.");

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Reviews.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(r => r.CompanyProfileId == companyId.Value);

        if (isDeleted.HasValue)
            query = query.Where(r => r.IsDeleted == isDeleted.Value);

        if (rating.HasValue)
            query = query.Where(r => r.RatingScore == rating.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(r =>
                (r.ReviewText != null && EF.Functions.Like(r.ReviewText, term)) ||
                EF.Functions.Like(r.Candidate.FullName, term) ||
                EF.Functions.Like(r.Company.CompanyName, term));
        }

        return ServiceResult<PagedResponse<ReviewListItemDto>>.Ok(
            await ToPageAsync(query, page, pageSize, cancellationToken));
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
            review.Candidate.User?.Email ?? string.Empty,
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
        var normalized = ModerationReason.Normalize(reason);
        if (ModerationReason.ValidateOptional(normalized) is { } invalid)
            return invalid;

        var review = await dbContext.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Review not found.");

        if (review.IsDeleted)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Review is already deleted.");

        review.IsDeleted = true;
        review.DeletedAt = DateTime.UtcNow;
        review.DeletedByAdminId = adminUserId;
        review.DeletionReason = normalized;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} removed/hidden review {ReviewId}.", adminUserId, reviewId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RestoreAsync(
        int reviewId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var review = await dbContext.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Review not found.");

        if (!review.IsDeleted)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Review is not deleted.");

        review.IsDeleted = false;
        review.DeletedAt = null;
        review.DeletedByAdminId = null;
        review.DeletionReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} restored review {ReviewId}.", adminUserId, reviewId);
        return ServiceResult.Ok();
    }

    /// <summary>Counts, orders newest-first, pages and projects a review query.</summary>
    internal static async Task<PagedResponse<ReviewListItemDto>> ToPageAsync(
        IQueryable<Review> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToListItem)
            .ToListAsync(cancellationToken);

        return new PagedResponse<ReviewListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }
}
