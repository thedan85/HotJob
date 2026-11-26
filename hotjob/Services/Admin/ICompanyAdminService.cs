using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Users;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface ICompanyAdminService
{
    /// <param name="status">Optional approval-status filter; null lists every company.</param>
    Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
        int page = 1, int pageSize = 20, string? search = null, CompanyStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListPendingAsync(
        int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<CompanyDetailDto>> GetDetailAsync(
        int companyId, CancellationToken cancellationToken = default);

    Task<ServiceResult> ApproveAsync(
        int companyId, string adminUserId, CancellationToken cancellationToken = default);

    Task<ServiceResult> RejectAsync(
        int companyId, string adminUserId, string reason, CancellationToken cancellationToken = default);

    Task<ServiceResult> DeactivateAsync(
        int companyId, string adminUserId, string? reason = null, CancellationToken cancellationToken = default);

    Task<ServiceResult> ReactivateAsync(
        int companyId, string adminUserId, CancellationToken cancellationToken = default);
}
