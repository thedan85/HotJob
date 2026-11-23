using HotJob.Dtos;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

/// <summary>Admin moderation of employer accounts.</summary>
[ApiController]
[Route("api/admin/employers")]
public sealed class EmployerAdminController(IEmployerAdminService employerAdminService) : AdminControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<EmployerListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        FromResult(await employerAdminService.ListAsync(page, pageSize, search, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<EmployerDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken) =>
        FromResult(await employerAdminService.GetDetailAsync(id, cancellationToken));

    /// <summary>Approve an employer membership. No body.</summary>
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Approve(int id, CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => employerAdminService.ApproveAsync(id, adminId, cancellationToken));

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Reject(
        int id,
        [FromBody] RejectEmployerRequest request,
        CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => employerAdminService.RejectAsync(id, adminId, request.Reason, cancellationToken));

    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Deactivate(
        int id,
        [FromBody] RejectEmployerRequest request,
        CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => employerAdminService.DeactivateAsync(id, adminId, request.Reason, cancellationToken));

    /// <summary>Reactivate an employer account. No body.</summary>
    [HttpPost("{id:int}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => employerAdminService.ReactivateAsync(id, adminId, cancellationToken));
}
