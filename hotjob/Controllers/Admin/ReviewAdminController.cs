using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace hotjob.Controllers.Admin;

[ApiController]
[Route("api/admin/reviews")]
public sealed class ReviewAdminController(IReviewAdminService reviewAdminService) : AdminControllerBase
{
    /// <summary>
    /// Company reviews; filter by visibility (<paramref name="isDeleted"/>), company, and
    /// <paramref name="reported"/> = true for the report queue (most-reported first).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ReviewListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isDeleted = null,
        [FromQuery] int? companyId = null,
        [FromQuery] bool reported = false,
        CancellationToken cancellationToken = default) =>
        OkOrFailure(await reviewAdminService.ListAsync(page, pageSize, isDeleted, companyId, reported, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ReviewDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken) =>
        OkOrFailure(await reviewAdminService.GetDetailAsync(id, cancellationToken));

    /// <summary>Every report filed against the review, newest first.</summary>
    [HttpGet("{id:int}/reports")]
    [ProducesResponseType(typeof(IReadOnlyList<ReviewReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReports(int id, CancellationToken cancellationToken) =>
        OkOrFailure(await reviewAdminService.GetReportsAsync(id, cancellationToken));

    /// <summary>Keeps the review visible and closes its pending reports as Dismissed.</summary>
    [HttpPost("{id:int}/reports/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DismissReports(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DismissReportsRequest? request,
        CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await reviewAdminService.DismissReportsAsync(id, adminId, request?.Note, cancellationToken));
    }

    /// <summary>
    /// Hides (soft-deletes) a review and closes its pending reports as Upheld. The body with a reason is optional.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteReviewRequest? request,
        CancellationToken cancellationToken)
    {
        if (CurrentAdminUserId is not { } adminId)
        {
            return MissingAdminIdentity();
        }

        return NoContentOrFailure(await reviewAdminService.DeleteAsync(id, adminId, request?.Reason, cancellationToken));
    }
}
