using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

[ApiController]
[Route("api/admin/jobs")]
public sealed class JobAdminController(IJobAdminService jobAdminService) : AdminControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<JobListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        Ok(await jobAdminService.ListAsync(page, pageSize, search, cancellationToken));

    [HttpGet("pending")]
    [ProducesResponseType(typeof(PagedResponse<JobListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        Ok(await jobAdminService.ListPendingAsync(page, pageSize, search, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(JobDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken)
    {
        var result = await jobAdminService.GetDetailAsync(id, cancellationToken);
        return result.Success ? Ok(result.Data) : NotFound(result.Errors);
    }

    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(
        int id,
        [FromBody] ApproveJobRequest request,
        CancellationToken cancellationToken)
    {
        var result = await jobAdminService.ApproveAsync(id, request.AdminUserId, cancellationToken);
        return result.Success ? NoContent() : NotFound(result.Errors);
    }

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] RejectJobRequest request,
        CancellationToken cancellationToken)
    {
        var result = await jobAdminService.RejectAsync(id, request.AdminUserId, request.Reason, cancellationToken);
        return result.Success ? NoContent() : NotFound(result.Errors);
    }
}
