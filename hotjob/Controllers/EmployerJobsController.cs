using System.Security.Claims;
using HotJob.Dtos;
using hotjob.DTOs.Job;
using hotjob.Models.Job;
using hotjob.Services.Common;
using hotjob.Services.Job;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>Employer job management (2.2): CRUD postings, assign categories/skills. Requires Employer role, active membership, approved company.</summary>
[ApiController]
[Route("api/employer/jobs")]
[Authorize(Roles = "Employer", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class EmployerJobsController(IJobService jobService) : ControllerBase
{
    /// <summary>2.2.1 Create a job posting (Draft default, or Published). Skills/categories validated inline.</summary>
    /// <response code="201">Created. Returns the new job detail.</response>
    /// <response code="400">Validation failed (title/description, salary range, future expiry, unknown skill/category).</response>
    /// <response code="401">Not an active employer of an approved company.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<JobDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromBody] CreateJobRequest request,
        CancellationToken cancellationToken)
    {
        var result = await jobService.CreateAsync(CurrentUserId(), request, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        return StatusCode(StatusCodes.Status201Created, OkPayload(result.Data!));
    }

    /// <summary>List own-company jobs with optional status/search filter (server-paginated).</summary>
    /// <response code="200">Paged job list with application counts.</response>
    /// <response code="401">Not an active employer of an approved company.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<JobListItemResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] JobStatus? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await jobService.ListAsync(CurrentUserId(), status, search, page, pageSize, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        return Ok(OkPayload(result.Data!));
    }

    /// <summary>Get one own-company job with skills, categories, application count.</summary>
    /// <response code="200">Job detail.</response>
    /// <response code="401">Not an active employer of an approved company.</response>
    /// <response code="404">Job not found in own company.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<JobDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var result = await jobService.GetAsync(CurrentUserId(), id, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        return Ok(OkPayload(result.Data!));
    }

    /// <summary>2.2.2 Update a job (scalar fields + atomic replace of skills/categories).</summary>
    /// <response code="200">Updated job detail.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="401">Not an active employer of an approved company.</response>
    /// <response code="404">Job not found in own company.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<JobDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateJobRequest request,
        CancellationToken cancellationToken)
    {
        var result = await jobService.UpdateAsync(CurrentUserId(), id, request, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        return Ok(OkPayload(result.Data!));
    }

    /// <summary>2.2.3 Delete a Draft job (hard delete) or close a Published job (soft close + mass-reject open applications).</summary>
    /// <response code="200">Returns { id, closed, status }.</response>
    /// <response code="401">Not an active employer of an approved company.</response>
    /// <response code="404">Job not found in own company.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<JobDeleteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOrClose(int id, CancellationToken cancellationToken)
    {
        var result = await jobService.DeleteOrCloseAsync(CurrentUserId(), id, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        return Ok(OkPayload(result.Data!));
    }

    /// <summary>2.2.4 Replace the category set of a job.</summary>
    /// <response code="200">Updated job detail.</response>
    /// <response code="400">Unknown category id.</response>
    /// <response code="401">Not an active employer of an approved company.</response>
    /// <response code="404">Job not found in own company.</response>
    [HttpPut("{id:int}/categories")]
    [ProducesResponseType(typeof(ApiResponse<JobDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignCategories(
        int id,
        [FromBody] AssignCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await jobService.AssignCategoriesAsync(CurrentUserId(), id, request, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        return Ok(OkPayload(result.Data!));
    }

    /// <summary>2.2.4 Replace the skill set (+ importance weights 0-10) of a job.</summary>
    /// <response code="200">Updated job detail.</response>
    /// <response code="400">Bad weight or unknown skill id.</response>
    /// <response code="401">Not an active employer of an approved company.</response>
    /// <response code="404">Job not found in own company.</response>
    [HttpPut("{id:int}/skills")]
    [ProducesResponseType(typeof(ApiResponse<JobDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignSkills(
        int id,
        [FromBody] AssignSkillsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await jobService.AssignSkillsAsync(CurrentUserId(), id, request, cancellationToken);
        if (!result.Success)
        {
            return MapError(result);
        }

        return Ok(OkPayload(result.Data!));
    }

    private string CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private static ApiResponse<T> OkPayload<T>(T data) => new() { Success = true, Data = data };

    private static IActionResult MapError(ServiceResult result)
    {
        ApiResponse<object> payload = new() { Success = false, Errors = result.Errors.ToList() };
        return result.ErrorType switch
        {
            ServiceErrorType.Conflict => new ConflictObjectResult(payload),
            ServiceErrorType.NotFound => new NotFoundObjectResult(payload),
            ServiceErrorType.Unauthorized => new UnauthorizedObjectResult(payload),
            _ => new BadRequestObjectResult(payload)
        };
    }
}
