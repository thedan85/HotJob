using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Users;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface ICompanyAdminService
{
    /// <summary>Lists companies with optional search, verification-status and activity filters.</summary>
    Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CompanyStatus? status = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    /// <summary>Shortcut for <see cref="ListAsync"/> with <see cref="CompanyStatus.Pending"/>.</summary>
    Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListPendingAsync(
        int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<CompanyDetailDto>> GetDetailAsync(
        int companyId, CancellationToken cancellationToken = default);

    /// <summary>Reviews of one company, newest first. Deleted reviews are excluded unless <paramref name="includeDeleted"/>.</summary>
    Task<ServiceResult<PagedResponse<ReviewListItemDto>>> GetCompanyReviewsAsync(
        int companyId,
        int page = 1,
        int pageSize = 20,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    /// <summary>Pending/Rejected → Approved. Already approved → Conflict.</summary>
    Task<ServiceResult> ApproveAsync(
        int companyId, string adminUserId, CancellationToken cancellationToken = default);

    /// <summary>Pending/Approved → Rejected (revokes approval). Already rejected → Conflict. Reason required, ≤500.</summary>
    Task<ServiceResult> RejectAsync(
        int companyId, string adminUserId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Active → inactive with optional reason (≤500). Already inactive → Conflict.</summary>
    Task<ServiceResult> DeactivateAsync(
        int companyId, string adminUserId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>Inactive → active, clearing deactivation metadata. Already active → Conflict.</summary>
    Task<ServiceResult> ReactivateAsync(
        int companyId, string adminUserId, CancellationToken cancellationToken = default);
}
