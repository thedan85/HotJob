using HotJob.Dtos;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace hotjob.Controllers.Admin;

/// <summary>Admin moderation of candidate reviews.</summary>
[ApiController]
[Route("api/admin/reviews")]
public sealed class ReviewAdminController(IReviewAdminService reviewAdminService) : AdminControllerBase
{
    /// <summary>Lists reviews filtered by company, deletion state, exact rating (1–5) and free-text search.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<ReviewListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? companyId = null,
        [FromQuery] bool? isDeleted = null,
        [FromQuery] int? rating = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        FromResult(await reviewAdminService.ListAsync(
            page, pageSize, companyId, isDeleted, rating, search, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ReviewDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken) =>
        FromResult(await reviewAdminService.GetDetailAsync(id, cancellationToken));

    /// <summary>Soft-delete (hide) a review. Body with an optional reason may be omitted.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Delete(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteReviewRequest? request,
        CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => reviewAdminService.DeleteAsync(id, adminId, request?.Reason, cancellationToken));

    /// <summary>Restore a soft-deleted review. No body.</summary>
    [HttpPost("{id:int}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Restore(int id, CancellationToken cancellationToken) =>
        AsAdminAsync(adminId => reviewAdminService.RestoreAsync(id, adminId, cancellationToken));
}
