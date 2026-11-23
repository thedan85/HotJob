using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface IReviewAdminService
{
    /// <summary>Lists reviews newest-first. <paramref name="rating"/> is an exact 1–5 match; <paramref name="search"/> matches review text, candidate name or company name.</summary>
    Task<ServiceResult<PagedResponse<ReviewListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        int? companyId = null,
        bool? isDeleted = null,
        int? rating = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ReviewDetailDto>> GetDetailAsync(
        int reviewId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes (hides) a review. Already deleted → Conflict.</summary>
    Task<ServiceResult> DeleteAsync(
        int reviewId, string adminUserId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>Restores a soft-deleted review. Not deleted → Conflict.</summary>
    Task<ServiceResult> RestoreAsync(
        int reviewId, string adminUserId, CancellationToken cancellationToken = default);
}
