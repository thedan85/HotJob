using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Employer;

/// <summary>Company review viewing for employers backed by EF Core.</summary>
/// <param name="db">Application database context.</param>
/// <param name="logger">Logger for review access.</param>
public sealed class ReviewService(ApplicationDbContext db, ILogger<ReviewService> logger) : IReviewService
{
    public async Task<ServiceResult<PagedResponse<ReviewListItemDto>>> GetCompanyReviewsAsync(
        string? userId,
        int page,
        int pageSize,
        int? rating = null,
        CancellationToken cancellationToken = default)
    {
        var employer = await RequireActiveEmployerAsync(userId, cancellationToken);
        if (!employer.Success)
            return ServiceResult<PagedResponse<ReviewListItemDto>>.Fail(employer.ErrorType!.Value, employer.Errors);

        // Validate pagination parameters
        if (page < 1)
            return ServiceResult<PagedResponse<ReviewListItemDto>>.Fail(
                ServiceErrorType.Validation, "Page number must be at least 1.");
        if (pageSize < 1 || pageSize > 100)
            return ServiceResult<PagedResponse<ReviewListItemDto>>.Fail(
                ServiceErrorType.Validation, "Page size must be between 1 and 100.");
        if (rating is < 1 or > 5)
            return ServiceResult<PagedResponse<ReviewListItemDto>>.Fail(
                ServiceErrorType.Validation, "Rating filter must be between 1 and 5.");

        var companyId = employer.Data!.CompanyProfileId;

        // Admin-deleted (moderated) reviews are never shown to the company.
        var query = db.Reviews
            .AsNoTracking()
            .Where(r => r.CompanyProfileId == companyId && !r.IsDeleted);
        if (rating.HasValue)
            query = query.Where(r => r.RatingScore == rating.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReviewListItemDto(
                r.Id,
                r.CandidateId,
                r.Candidate.FullName,
                r.RatingScore,
                r.ReviewText,
                r.CreatedAt))
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "Employer {UserId} viewed reviews page {Page} for company {CompanyId}.",
            userId, page, companyId);

        return ServiceResult<PagedResponse<ReviewListItemDto>>.Ok(new PagedResponse<ReviewListItemDto>
        {
            Items = reviews,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<CompanyRatingStatsDto>> GetCompanyRatingStatsAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var employer = await RequireActiveEmployerAsync(userId, cancellationToken);
        if (!employer.Success)
            return ServiceResult<CompanyRatingStatsDto>.Fail(employer.ErrorType!.Value, employer.Errors);

        var companyId = employer.Data!.CompanyProfileId;

        // Get all non-deleted reviews for the company
        var reviews = await db.Reviews
            .AsNoTracking()
            .Where(r => r.CompanyProfileId == companyId && !r.IsDeleted)
            .Select(r => r.RatingScore)
            .ToListAsync(cancellationToken);

        var totalReviews = reviews.Count;
        var averageRating = totalReviews > 0 ? reviews.Average() : 0.0;

        var stats = new CompanyRatingStatsDto
        {
            TotalReviews = totalReviews,
            AverageRating = Math.Round(averageRating, 2),
            FiveStarCount = reviews.Count(r => r == 5),
            FourStarCount = reviews.Count(r => r == 4),
            ThreeStarCount = reviews.Count(r => r == 3),
            TwoStarCount = reviews.Count(r => r == 2),
            OneStarCount = reviews.Count(r => r == 1)
        };

        logger.LogInformation(
            "Employer {UserId} viewed rating stats for company {CompanyId}.",
            userId, companyId);

        return ServiceResult<CompanyRatingStatsDto>.Ok(stats);
    }

    private async Task<ServiceResult<global::hotjob.Models.Users.Employer>> RequireActiveEmployerAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(
                ServiceErrorType.Unauthorized, "Authentication required.");

        var employer = await db.Employers
            .AsNoTracking()
            .Include(e => e.Company)
            .SingleOrDefaultAsync(e => e.UserId == userId, cancellationToken);

        // Same rules as every other employer feature (deactivated account/company, removed member are blocked).
        // Reading reviews is allowed while the company is still Pending/Rejected, like the company page.
        return EmployerGuard.Evaluate(employer, requireApprovedCompany: false);
    }
}
