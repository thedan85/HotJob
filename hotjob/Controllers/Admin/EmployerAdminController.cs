using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

[ApiController]
[Route("api/admin/employers")]
public sealed class EmployerAdminController(IEmployerAdminService employerAdminService) : AdminControllerBase
{
    /// <summary>All employer accounts, optionally filtered by membership status (Pending, Active, Rejected).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<EmployerListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] MembershipStatus? status = null,
        CancellationToken cancellationToken = default) =>
        OkOrFailure(await employerAdminService.ListAsync(page, pageSize, search, status, cancellationToken));

    [HttpGet("pending")]
    [ProducesResponseType(typeof(PagedResponse<EmployerListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        OkOrFailure(await employerAdminService.ListPendingAsync(page, pageSize, search, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(EmployerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken) =>
        OkOrFailure(await employerAdminService.GetDetailAsync(id, cancellationToken));

    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await employerAdminService.ApproveAsync(id, adminId, cancellationToken));
    }

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] RejectEmployerRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await employerAdminService.RejectAsync(id, adminId, request.Reason, cancellationToken));
    }

    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(
        int id,
        [FromBody] RejectEmployerRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await employerAdminService.DeactivateAsync(id, adminId, request.Reason, cancellationToken));
    }

    [HttpPost("{id:int}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await employerAdminService.ReactivateAsync(id, adminId, cancellationToken));
    }
}
