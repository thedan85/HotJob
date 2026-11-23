using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

/// <summary>Company profile and team member management for the caller's own company.</summary>
public interface IEmployerCompanyService
{
    /// <summary>Company profile with members and the caller's owner flag.</summary>
    /// <param name="userId">Caller user id; must be an active employer member.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyPageResponse>> GetPageAsync(
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>Update own-company profile fields (owner only).</summary>
    /// <param name="userId">Caller user id; must be the company owner.</param>
    /// <param name="request">Fields to update; null means no change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyDetailResponse>> UpdateAsync(
        string? userId,
        UpdateCompanyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Approve a pending join request (owner only, Pending→Active).</summary>
    /// <param name="userId">Caller user id; must be the company owner.</param>
    /// <param name="memberId">Employer row id to approve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyMemberDto>> ApproveMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>Reject a pending join request (owner only, Pending→Rejected).</summary>
    /// <param name="userId">Caller user id; must be the company owner.</param>
    /// <param name="memberId">Employer row id to reject.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyMemberDto>> RejectMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-remove an active member from the company (owner only, Active to Removed). Their jobs stay with the company.</summary>
    /// <param name="userId">Caller user id; must be the company owner.</param>
    /// <param name="memberId">Employer row id to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyMemberDto>> RemoveMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>Restore a soft-removed member (owner only, Removed to Active).</summary>
    /// <param name="userId">Caller user id; must be the company owner.</param>
    /// <param name="memberId">Employer row id to restore.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyMemberDto>> RestoreMemberAsync(
        string? userId,
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>Member-created jobs waiting for the owner's approval (owner only), oldest first.</summary>
    /// <param name="userId">Caller user id; must be the company owner.</param>
    /// <param name="page">Page number (clamped to at least 1).</param>
    /// <param name="pageSize">Items per page (clamped to 1-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<PagedResponse<CompanyPendingJobDto>>> ListPendingJobsAsync(
        string? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Owner approves a member job (PendingCompanyApproval to PendingApproval, i.e. forwarded to admin).</summary>
    /// <param name="userId">Caller user id; must be the company owner.</param>
    /// <param name="jobId">Job id in the owner's company.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyPendingJobDto>> ApproveJobAsync(
        string? userId,
        int jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Owner rejects a member job with a reason (PendingCompanyApproval to Rejected).</summary>
    /// <param name="userId">Caller user id; must be the company owner.</param>
    /// <param name="jobId">Job id in the owner's company.</param>
    /// <param name="request">Rejection reason (required, max 1000 chars).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<CompanyPendingJobDto>> RejectJobAsync(
        string? userId,
        int jobId,
        RejectCompanyJobRequest request,
        CancellationToken cancellationToken = default);
}
