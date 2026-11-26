using hotjob.Models;

namespace hotjob.Repositories.Review;

public interface IReviewRepository
{
    /// <summary>
    /// Get reviews for a specific company with pagination.
    /// </summary>
    Task<(IReadOnlyList<Models.Review> Items, int TotalCount)> ListByCompanyAsync(
        int companyId,
        bool includeDeleted,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single review by ID with full details.
    /// </summary>
    Task<Models.Review?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get rating statistics for a company.
    /// </summary>
    Task<(double AverageRating, int TotalCount)> GetCompanyRatingStatsAsync(
        int companyId,
        CancellationToken cancellationToken = default);
}
