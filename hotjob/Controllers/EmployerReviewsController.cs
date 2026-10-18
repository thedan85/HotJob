using System.Security.Claims;
using HotJob.Dtos;
using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
using hotjob.Services.Employer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>Company review viewing for employers (read-only, own company only).</summary>
[ApiController]
[Route("api/employer/reviews")]
[Authorize(Roles = "Employer", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class EmployerReviewsController(IReviewService reviewService) : ApiControllerBase
{
    /// <summary>Get paginated reviews for the employer's company.</summary>
    /// <param name="page">Page number (default: 1).</param>
    /// <param name="pageSize">Items per page (default: 10, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Paginated list of reviews.</response>
    /// <response code="400">Invalid pagination parameters.</response>
    /// <response code="401">Not an active employer.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<ReviewListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCompanyReviews(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await reviewService.GetCompanyReviewsAsync(
            CurrentUserId(), page, pageSize, cancellationToken);
        return FromResult(result);
    }

    /// <summary>Get aggregated rating statistics for the employer's company.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Rating statistics.</response>
    /// <response code="401">Not an active employer.</response>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ApiResponse<CompanyRatingStatsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCompanyRatingStats(CancellationToken cancellationToken)
    {
        var result = await reviewService.GetCompanyRatingStatsAsync(CurrentUserId(), cancellationToken);
        return FromResult(result);
    }

    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
}
