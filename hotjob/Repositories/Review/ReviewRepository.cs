using hotjob.Data;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Repositories.Review;

public sealed class ReviewRepository(ApplicationDbContext dbContext) : IReviewRepository
{
    public async Task<(IReadOnlyList<Models.Review> Items, int TotalCount)> ListByCompanyAsync(
        int companyId,
        bool includeDeleted,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = DetailsQuery().Where(review => review.CompanyProfileId == companyId);

        if (!includeDeleted)
        {
            query = query.Where(review => !review.IsDeleted);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<Models.Review?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return DetailsQuery()
            .Where(review => review.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(double AverageRating, int TotalCount)> GetCompanyRatingStatsAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var reviews = await dbContext.Reviews
            .AsNoTracking()
            .Where(review => review.CompanyProfileId == companyId && !review.IsDeleted)
            .Select(review => review.RatingScore)
            .ToListAsync(cancellationToken);

        if (reviews.Count == 0)
        {
            return (0.0, 0);
        }

        var average = reviews.Average();
        return (average, reviews.Count);
    }

    private IQueryable<Models.Review> DetailsQuery() =>
        dbContext.Reviews
            .AsNoTracking()
            .Include(review => review.Company)
            .Include(review => review.Candidate);
}
