using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Job;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

[ApiController]
[Route("api/admin/jobs")]
public sealed class JobAdminController(IJobAdminService jobAdminService) : AdminControllerBase
{
    /// <summary>All job postings, optionally filtered by status (e.g. PendingApproval, Published, Rejected).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<JobListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] JobStatus? status = null,
        [FromQuery] int? companyId = null,
        [FromQuery] bool? isFeatured = null,
        [FromQuery] bool? isModerated = null,
        CancellationToken cancellationToken = default) =>
        OkOrFailure(await jobAdminService.ListAsync(new AdminJobQuery
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            Status = status,
            CompanyId = companyId,
            IsFeatured = isFeatured,
            IsModerated = isModerated
        }, cancellationToken));

    [HttpGet("pending")]
    [ProducesResponseType(typeof(PagedResponse<JobListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        OkOrFailure(await jobAdminService.ListPendingAsync(page, pageSize, search, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(JobDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken) =>
        OkOrFailure(await jobAdminService.GetDetailAsync(id, cancellationToken));

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

        return NoContentOrFailure(await jobAdminService.ApproveAsync(id, adminId, cancellationToken));
    }

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] RejectJobRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await jobAdminService.RejectAsync(id, adminId, request.Reason, cancellationToken));
    }

    /// <summary>Takes a violating posting down (Closed, kept on record). Returns the number of applications closed.</summary>
    [HttpPost("{id:int}/remove")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(
        int id,
        [FromBody] RemoveJobRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return OkOrFailure(await jobAdminService.RemoveAsync(id, adminId, request.Reason, cancellationToken));
    }
}
