using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

/// <summary>Company review viewing for employers (read-only, own company only).</summary>
public interface IReviewService
{
    /// <summary>Get paginated reviews for the employer's company.</summary>
    /// <param name="userId">Caller user id; must be an active employer member.</param>
    /// <param name="page">Page number (1-indexed).</param>
    /// <param name="pageSize">Items per page (1-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<PagedResponse<ReviewListItemDto>>> GetCompanyReviewsAsync(
        string? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Get aggregated rating statistics for the employer's company.</summary>
    /// <param name="userId">Caller user id; must be an active employer member.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyRatingStatsDto>> GetCompanyRatingStatsAsync(
        string? userId,
        CancellationToken cancellationToken = default);
}
