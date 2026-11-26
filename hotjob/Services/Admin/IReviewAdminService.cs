using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface IReviewAdminService
{
    Task<ServiceResult<PagedResponse<ReviewListItemDto>>> ListAsync(
        int page = 1, int pageSize = 20, bool? isDeleted = null, int? companyId = null,
        bool reportedOnly = false, CancellationToken cancellationToken = default);

    /// <summary>All reports filed against a review, newest first.</summary>
    Task<ServiceResult<IReadOnlyList<ReviewReportDto>>> GetReportsAsync(
        int reviewId, CancellationToken cancellationToken = default);

    /// <summary>Keeps the review visible and closes every pending report on it as Dismissed.</summary>
    Task<ServiceResult> DismissReportsAsync(
        int reviewId, string adminUserId, string? note = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReviewDetailDto>> GetDetailAsync(
        int reviewId, CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteAsync(
        int reviewId, string adminUserId, string? reason = null, CancellationToken cancellationToken = default);
}
