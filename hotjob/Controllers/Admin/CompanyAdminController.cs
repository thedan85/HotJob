using HotJob.Dtos;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace hotjob.Controllers.Admin;

/// <summary>Admin moderation of company profiles.</summary>
[ApiController]
[Route("api/admin/companies")]
public sealed class CompanyAdminController(ICompanyAdminService companyAdminService) : AdminControllerBase
{
    /// <summary>Lists companies, optionally filtered by search text, verification status and activity.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<CompanyListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] CompanyStatus? status = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default) =>
        FromResult(await companyAdminService.ListAsync(page, pageSize, search, status, isActive, cancellationToken));

    /// <summary>Lists companies awaiting verification.</summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<CompanyListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        FromResult(await companyAdminService.ListPendingAsync(page, pageSize, search, cancellationToken));

    /// <summary>Company detail with owner, member, job and review aggregates.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<CompanyDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken) =>
        FromResult(await companyAdminService.GetDetailAsync(id, cancellationToken));

    /// <summary>Reviews of one company; deleted reviews are excluded unless <paramref name="includeDeleted"/>.</summary>
    [HttpGet("{id:int}/reviews")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<ReviewListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviews(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeDeleted = false,
        CancellationToken cancellationToken = default) =>
        FromResult(await companyAdminService.GetCompanyReviewsAsync(id, page, pageSize, includeDeleted, cancellationToken));

    /// <summary>Approve a pending or previously rejected company. No body.</summary>
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Approve(int id, CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => companyAdminService.ApproveAsync(id, adminId, cancellationToken));

    /// <summary>Reject a pending company or revoke an approved one.</summary>
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reject(
        int id,
        [FromBody] RejectCompanyRequest request,
        CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => companyAdminService.RejectAsync(id, adminId, request.Reason, cancellationToken));

    /// <summary>Deactivate an active company. Body (with an optional reason) may be omitted.</summary>
    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Deactivate(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeactivateCompanyRequest? request,
        CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => companyAdminService.DeactivateAsync(id, adminId, request?.Reason, cancellationToken));

    /// <summary>Reactivate a deactivated company. No body.</summary>
    [HttpPost("{id:int}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => companyAdminService.ReactivateAsync(id, adminId, cancellationToken));
}
