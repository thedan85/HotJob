using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

public sealed class ReviewAdminService(
    ApplicationDbContext dbContext,
    ILogger<ReviewAdminService> logger) : IReviewAdminService
{
    public async Task<ServiceResult<PagedResponse<ReviewListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        bool? isDeleted = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Reviews
            .AsNoTracking()
            .Include(r => r.Company)
            .Include(r => r.Candidate)
            .AsQueryable();

        if (isDeleted.HasValue)
        {
            query = query.Where(r => r.IsDeleted == isDeleted.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
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
                r.DeletedByAdminId))
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
        var review = await dbContext.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Review not found.");

        review.IsDeleted = true;
        review.DeletedAt = DateTime.UtcNow;
        review.DeletedByAdminId = adminUserId;
        review.DeletionReason = reason;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} removed/hidden review {ReviewId}.", adminUserId, reviewId);
        return ServiceResult.Ok();
    }
}
