using HotJob.Dtos;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

/// <summary>Admin review of job postings.</summary>
[ApiController]
[Route("api/admin/jobs")]
public sealed class JobAdminController(IJobAdminService jobAdminService) : AdminControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<JobListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        FromResult(await jobAdminService.ListAsync(page, pageSize, search, cancellationToken));

    /// <summary>Jobs waiting for admin review (status PendingApproval).</summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<JobListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        FromResult(await jobAdminService.ListPendingAsync(page, pageSize, search, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<JobDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken) =>
        FromResult(await jobAdminService.GetDetailAsync(id, cancellationToken));

    /// <summary>Approve a job in PendingApproval. No body.</summary>
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Approve(int id, CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => jobAdminService.ApproveAsync(id, adminId, cancellationToken));

    /// <summary>Reject a job in PendingApproval with a reason.</summary>
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reject(
        int id,
        [FromBody] RejectJobRequest request,
        CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => jobAdminService.RejectAsync(id, adminId, request.Reason, cancellationToken));
}
