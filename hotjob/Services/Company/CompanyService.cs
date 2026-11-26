using hotjob.Data;
using hotjob.DTOs.Company;
using hotjob.DTOs.Job;
using hotjob.Models;
using hotjob.Models.Users;
using hotjob.Repositories.Company;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Company;

public sealed class CompanyService(
    ICompanyRepository repository,
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<CompanyService> logger) : ICompanyService
{
    public const int PageSize = 20;
    public const int ReviewPageSize = 5;

    public async Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
        CompanyListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
            return ServiceResult<PagedResponse<CompanyListItemDto>>.Fail(ServiceErrorType.Validation, "Query is required.");

        var page = Math.Max(query.Page, 1);
        var (items, total) = await repository.ListApprovedAsync(
            query.Search, query.Industries, query.Locations, page, PageSize, cancellationToken);

        var ratings = await LoadRatingsAsync(items.Select(company => company.Id).ToList(), cancellationToken);

        logger.LogDebug("Listed {Count} companies (page {Page}, total {Total}).", items.Count, page, total);

        return ServiceResult<PagedResponse<CompanyListItemDto>>.Ok(new PagedResponse<CompanyListItemDto>
        {
            Items = items.Select(company =>
            {
                var rating = ratings.GetValueOrDefault(company.Id);
                return new CompanyListItemDto(
                    company.Id,
                    company.CompanyName,
                    company.Industry,
                    company.Location,
                    company.Scale,
                    company.LogoUrl,
                    company.Description,
                    rating.Count == 0 ? null : Math.Round(rating.Average, 1),
                    rating.Count);
            }).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = PageSize
        });
    }

    public async Task<ServiceResult<CompanyFilterOptionsDto>> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var (industries, locations) = await repository.ListFilterOptionsAsync(cancellationToken);
        return ServiceResult<CompanyFilterOptionsDto>.Ok(new CompanyFilterOptionsDto(industries, locations));
    }

    public async Task<ServiceResult<CompanyDetailDto>> GetDetailAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
            return ServiceResult<CompanyDetailDto>.Fail(ServiceErrorType.NotFound, "Company not found.");

        var company = await repository.GetApprovedByIdAsync(id, cancellationToken);
        if (company is null)
            return ServiceResult<CompanyDetailDto>.Fail(ServiceErrorType.NotFound, "Company not found.");

        var ownerName = await repository.GetOwnerNameAsync(id, cancellationToken);
        var rating = await dbContext.Reviews
            .AsNoTracking()
            .Where(review => review.CompanyProfileId == id && !review.IsDeleted)
            .GroupBy(review => review.CompanyProfileId)
            .Select(group => new
            {
                Average = group.Average(review => (double)review.RatingScore),
                Count = group.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return ServiceResult<CompanyDetailDto>.Ok(new CompanyDetailDto(
            company.Id,
            company.CompanyName,
            company.Industry,
            company.Location,
            company.Scale,
            company.LogoUrl,
            company.Description,
            company.Website,
            ownerName,
            rating is null ? null : Math.Round(rating.Average, 1),
            rating?.Count ?? 0));
    }

    public async Task<ServiceResult<PagedResponse<CompanyReviewDto>>> GetReviewsAsync(
        int companyId,
        int page,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        var (items, total) = await repository.ListReviewsAsync(companyId, page, ReviewPageSize, cancellationToken);

        return ServiceResult<PagedResponse<CompanyReviewDto>>.Ok(new PagedResponse<CompanyReviewDto>
        {
            Items = items.Select(review => new CompanyReviewDto(
                review.Id,
                review.RatingScore,
                review.ReviewText,
                review.Candidate?.FullName ?? "Anonymous",
                review.CreatedAt)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = ReviewPageSize
        });
    }

    public async Task<ServiceResult<CompanyReviewDto>> CreateReviewAsync(
        string? userId,
        int companyId,
        CreateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<CompanyReviewDto>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        if (request is null)
            return ServiceResult<CompanyReviewDto>.Fail(ServiceErrorType.Validation, "Review data is required.");

        if (request.RatingScore is < 1 or > 5)
            return ServiceResult<CompanyReviewDto>.Fail(ServiceErrorType.Validation, "Rating must be between 1 and 5.");

        var reviewText = string.IsNullOrWhiteSpace(request.ReviewText) ? null : request.ReviewText.Trim();
        if (reviewText is { Length: > 2000 })
            return ServiceResult<CompanyReviewDto>.Fail(
                ServiceErrorType.Validation, "Review text must be 2000 characters or fewer.");

        var candidate = await dbContext.Candidates
            .AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new { row.Id, row.FullName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidate is null)
            return ServiceResult<CompanyReviewDto>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.");

        var companyExists = await dbContext.CompanyProfiles
            .AsNoTracking()
            .Where(CompanyVisibility.IsPublic)
            .AnyAsync(company => company.Id == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (!companyExists)
            return ServiceResult<CompanyReviewDto>.Fail(ServiceErrorType.NotFound, "Company not found.");

        if (!await HasAppliedToCompanyAsync(candidate.Id, companyId, cancellationToken).ConfigureAwait(false))
            return ServiceResult<CompanyReviewDto>.Fail(ServiceErrorType.Validation, NeverAppliedMessage);

        var alreadyReviewed = await dbContext.Reviews
            .AsNoTracking()
            .AnyAsync(
                review => review.CompanyProfileId == companyId && review.CandidateId == candidate.Id,
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadyReviewed)
            return ServiceResult<CompanyReviewDto>.Fail(
                ServiceErrorType.Conflict, "You already reviewed this company.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var review = new Review
        {
            CompanyProfileId = companyId,
            CandidateId = candidate.Id,
            RatingScore = request.RatingScore,
            ReviewText = reviewText,
            CreatedAt = now
        };
        dbContext.Reviews.Add(review);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Unique (company, candidate) index: a concurrent submit already stored one.
            dbContext.Entry(review).State = EntityState.Detached;
            return ServiceResult<CompanyReviewDto>.Fail(
                ServiceErrorType.Conflict, "You already reviewed this company.");
        }

        logger.LogInformation("Candidate {CandidateId} reviewed company {CompanyId}.", candidate.Id, companyId);

        return ServiceResult<CompanyReviewDto>.Ok(new CompanyReviewDto(
            review.Id,
            review.RatingScore,
            review.ReviewText,
            candidate.FullName,
            review.CreatedAt));
    }

    public async Task<ServiceResult> ReportReviewAsync(
        string? userId,
        int reviewId,
        ReportReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult.Fail(ServiceErrorType.Unauthorized, "Sign in to report a review.");
        if (request is null || !Enum.IsDefined(request.Reason))
            return ServiceResult.Fail(ServiceErrorType.Validation, "Choose a reason for the report.");

        var details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim();
        if (details is { Length: > 500 })
            return ServiceResult.Fail(ServiceErrorType.Validation, "Details must be 500 characters or fewer.");
        if (request.Reason == ReviewReportReason.Other && details is null)
            return ServiceResult.Fail(ServiceErrorType.Validation, "Describe the problem when choosing \"Other\".");

        // Only reviews the public can currently see can be reported.
        var review = await dbContext.Reviews
            .AsNoTracking()
            .Where(row => row.Id == reviewId
                && !row.IsDeleted
                && row.Company.Status == CompanyStatus.Approved
                && row.Company.IsActive)
            .Select(row => new { row.Id, AuthorUserId = row.Candidate.UserId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (review is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Review not found.");
        if (review.AuthorUserId == userId)
            return ServiceResult.Fail(ServiceErrorType.Validation, "You cannot report your own review.");

        var alreadyReported = await dbContext.ReviewReports
            .AsNoTracking()
            .AnyAsync(report => report.ReviewId == reviewId && report.ReporterUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyReported)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "You already reported this review.");

        var report = new ReviewReport
        {
            ReviewId = reviewId,
            ReporterUserId = userId,
            Reason = request.Reason,
            Details = details,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.ReviewReports.Add(report);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Unique (review, reporter) index: a concurrent submit already stored one.
            dbContext.Entry(report).State = EntityState.Detached;
            return ServiceResult.Fail(ServiceErrorType.Conflict, "You already reported this review.");
        }

        logger.LogInformation("User {UserId} reported review {ReviewId} ({Reason}).", userId, reviewId, request.Reason);
        return ServiceResult.Ok();
    }

    private const string NeverAppliedMessage =
        "You can review a company only after applying to one of its jobs.";

    /// <summary>
    /// Only candidates who applied to one of the company's jobs (any status, withdrawn included)
    /// may review it, so reviews come from people who actually went through its hiring process.
    /// </summary>
    private Task<bool> HasAppliedToCompanyAsync(int candidateId, int companyId, CancellationToken cancellationToken) =>
        dbContext.Applications
            .AsNoTracking()
            .AnyAsync(
                application => application.CandidateId == candidateId
                    && application.JobPosting.CompanyProfileId == companyId,
                cancellationToken);

    public async Task<ServiceResult<ReviewEligibilityDto>> GetReviewEligibilityAsync(
        string? userId,
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var mine = await GetMyReviewsAsync(userId, companyId, cancellationToken).ConfigureAwait(false);
        if (!mine.Success)
            return ServiceResult<ReviewEligibilityDto>.Fail(mine.ErrorType!.Value, mine.Errors);

        var existing = mine.Data!.FirstOrDefault();
        if (existing is not null)
            return ServiceResult<ReviewEligibilityDto>.Ok(
                new ReviewEligibilityDto(false, "You already reviewed this company.", existing));

        var companyIsPublic = await dbContext.CompanyProfiles
            .AsNoTracking()
            .Where(CompanyVisibility.IsPublic)
            .AnyAsync(company => company.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (!companyIsPublic)
            return ServiceResult<ReviewEligibilityDto>.Fail(ServiceErrorType.NotFound, "Company not found.");

        var candidateId = await dbContext.Candidates
            .AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => row.Id)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        var hasApplied = await HasAppliedToCompanyAsync(candidateId, companyId, cancellationToken).ConfigureAwait(false);

        return ServiceResult<ReviewEligibilityDto>.Ok(hasApplied
            ? new ReviewEligibilityDto(true, null, null)
            : new ReviewEligibilityDto(false, NeverAppliedMessage, null));
    }

    public async Task<ServiceResult<IReadOnlyList<MyReviewDto>>> GetMyReviewsAsync(
        string? userId,
        int? companyId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<IReadOnlyList<MyReviewDto>>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        var candidateId = await dbContext.Candidates
            .AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => (int?)row.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidateId is null)
            return ServiceResult<IReadOnlyList<MyReviewDto>>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.");

        // Scoped to the caller's CandidateId: nobody can list another candidate's reviews.
        var query = dbContext.Reviews
            .AsNoTracking()
            .Where(review => review.CandidateId == candidateId.Value);
        if (companyId.HasValue)
            query = query.Where(review => review.CompanyProfileId == companyId.Value);

        var reviews = await query
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Select(review => new MyReviewDto(
                review.Id,
                review.CompanyProfileId,
                review.Company.CompanyName,
                review.RatingScore,
                review.ReviewText,
                review.CreatedAt,
                review.IsDeleted))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<IReadOnlyList<MyReviewDto>>.Ok(reviews);
    }

    private async Task<Dictionary<int, (double Average, int Count)>> LoadRatingsAsync(
        IReadOnlyCollection<int> companyIds,
        CancellationToken cancellationToken)
    {
        if (companyIds.Count == 0)
        {
            return new Dictionary<int, (double Average, int Count)>();
        }

        var rows = await dbContext.Reviews
            .AsNoTracking()
            .Where(review => companyIds.Contains(review.CompanyProfileId) && !review.IsDeleted)
            .GroupBy(review => review.CompanyProfileId)
            .Select(group => new
            {
                CompanyId = group.Key,
                Average = group.Average(review => (double)review.RatingScore),
                Count = group.Count()
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(row => row.CompanyId, row => (row.Average, row.Count));
    }
}
