using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

[ApiController]
[Route("api/admin/employers")]
public sealed class EmployerAdminController(IEmployerAdminService employerAdminService) : AdminControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<EmployerListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        Ok(await employerAdminService.ListAsync(page, pageSize, search, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(EmployerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken)
    {
        var result = await employerAdminService.GetDetailAsync(id, cancellationToken);
        return result.Success ? Ok(result.Data) : NotFound(result.Errors);
    }

    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(
        int id,
        [FromBody] ApproveEmployerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await employerAdminService.ApproveAsync(id, request.AdminUserId, cancellationToken);
        return result.Success ? NoContent() : NotFound(result.Errors);
    }

    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] RejectEmployerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await employerAdminService.RejectAsync(id, request.AdminUserId, request.Reason, cancellationToken);
        return result.Success ? NoContent() : NotFound(result.Errors);
    }

    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        int id,
        [FromBody] RejectEmployerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await employerAdminService.DeactivateAsync(id, request.AdminUserId, request.Reason, cancellationToken);
        return result.Success ? NoContent() : NotFound(result.Errors);
    }

    [HttpPost("{id:int}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reactivate(
        int id,
        [FromBody] ApproveEmployerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await employerAdminService.ReactivateAsync(id, request.AdminUserId, cancellationToken);
        return result.Success ? NoContent() : NotFound(result.Errors);
    }
}
