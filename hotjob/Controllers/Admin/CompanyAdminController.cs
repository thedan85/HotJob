using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

[ApiController]
[Route("api/admin/companies")]
public sealed class CompanyAdminController(ICompanyAdminService companyAdminService) : AdminControllerBase
{
    /// <summary>All companies, optionally filtered by approval status (Pending, Approved, Rejected).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<CompanyListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] CompanyStatus? status = null,
        CancellationToken cancellationToken = default) =>
        OkOrFailure(await companyAdminService.ListAsync(page, pageSize, search, status, cancellationToken));

    [HttpGet("pending")]
    [ProducesResponseType(typeof(PagedResponse<CompanyListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        OkOrFailure(await companyAdminService.ListPendingAsync(page, pageSize, search, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CompanyDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken) =>
        OkOrFailure(await companyAdminService.GetDetailAsync(id, cancellationToken));

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

        return NoContentOrFailure(await companyAdminService.ApproveAsync(id, adminId, cancellationToken));
    }

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] RejectCompanyRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await companyAdminService.RejectAsync(id, adminId, request.Reason, cancellationToken));
    }

    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(
        int id,
        [FromBody] RejectCompanyRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await companyAdminService.DeactivateAsync(id, adminId, request.Reason, cancellationToken));
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

        return NoContentOrFailure(await companyAdminService.ReactivateAsync(id, adminId, cancellationToken));
    }
}
