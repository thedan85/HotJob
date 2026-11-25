using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

[ApiController]
[Route("api/admin/reviews")]
public sealed class ReviewAdminController(IReviewAdminService reviewAdminService) : AdminControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ReviewListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isDeleted = null,
        CancellationToken cancellationToken = default) =>
        Ok(await reviewAdminService.ListAsync(page, pageSize, isDeleted, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ReviewDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken)
    {
        var result = await reviewAdminService.GetDetailAsync(id, cancellationToken);
        return result.Success ? Ok(result.Data) : NotFound(result.Errors);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int id,
        [FromBody] DeleteReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await reviewAdminService.DeleteAsync(id, request.AdminUserId, request.Reason, cancellationToken);
        return result.Success ? NoContent() : NotFound(result.Errors);
    }
}
