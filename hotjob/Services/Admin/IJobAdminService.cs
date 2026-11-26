using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Job;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface IJobAdminService
{
    /// <param name="status">Optional status filter; null lists every job posting.</param>
    Task<ServiceResult<PagedResponse<JobListItemDto>>> ListAsync(
        int page = 1, int pageSize = 20, string? search = null, JobStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>Jobs filtered by search, status, company, featured and removed flags.</summary>
    Task<ServiceResult<PagedResponse<JobListItemDto>>> ListAsync(
        AdminJobQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<JobListItemDto>>> ListPendingAsync(
        int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<JobDetailDto>> GetDetailAsync(
        int jobId, CancellationToken cancellationToken = default);

    Task<ServiceResult> ApproveAsync(
        int jobId, string adminUserId, CancellationToken cancellationToken = default);

    Task<ServiceResult> RejectAsync(
        int jobId, string adminUserId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a violating posting down for good: Closed with the reason and admin recorded, open
    /// applications auto-rejected (history kept), candidates and the company's employers notified.
    /// Returns how many applications were rejected.
    /// </summary>
    Task<ServiceResult<int>> RemoveAsync(
        int jobId, string adminUserId, string reason, CancellationToken cancellationToken = default);
}
