using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface IReviewAdminService
{
    Task<ServiceResult<PagedResponse<ReviewListItemDto>>> ListAsync(
        int page = 1, int pageSize = 20, bool? isDeleted = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReviewDetailDto>> GetDetailAsync(
        int reviewId, CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteAsync(
        int reviewId, string adminUserId, string? reason = null, CancellationToken cancellationToken = default);
}
