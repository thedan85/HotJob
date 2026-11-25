using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface IEmployerAdminService
{
    Task<ServiceResult<PagedResponse<EmployerListItemDto>>> ListAsync(
        int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<EmployerDetailDto>> GetDetailAsync(
        int employerId, CancellationToken cancellationToken = default);

    Task<ServiceResult<EmployerDetailDto>> GetByUserIdAsync(
        string userId, CancellationToken cancellationToken = default);

    Task<ServiceResult> ApproveAsync(
        int employerId, string adminUserId, CancellationToken cancellationToken = default);

    Task<ServiceResult> RejectAsync(
        int employerId, string adminUserId, string reason, CancellationToken cancellationToken = default);

    Task<ServiceResult> DeactivateAsync(
        int employerId, string adminUserId, string? reason = null, CancellationToken cancellationToken = default);

    Task<ServiceResult> ReactivateAsync(
        int employerId, string adminUserId, CancellationToken cancellationToken = default);
}
