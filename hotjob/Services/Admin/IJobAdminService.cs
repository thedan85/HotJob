using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface IJobAdminService
{
    Task<ServiceResult<PagedResponse<JobListItemDto>>> ListAsync(
        int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<JobListItemDto>>> ListPendingAsync(
        int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<JobDetailDto>> GetDetailAsync(
        int jobId, CancellationToken cancellationToken = default);

    /// <summary>PendingApproval → Approved. Any other status (incl. PendingCompanyApproval) → Conflict.</summary>
    Task<ServiceResult> ApproveAsync(
        int jobId, string adminUserId, CancellationToken cancellationToken = default);

    /// <summary>PendingApproval → Rejected. Reason required, ≤500. Any other status → Conflict.</summary>
    Task<ServiceResult> RejectAsync(
        int jobId, string adminUserId, string? reason, CancellationToken cancellationToken = default);
}
