using hotjob.DTOs.Employer;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

/// <summary>Company profile and team member management for the caller's own company.</summary>
public interface ICompanyService
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
}
