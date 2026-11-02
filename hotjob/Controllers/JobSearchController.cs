using System.Security.Claims;
using HotJob.Dtos;
using hotjob.DTOs.Job;
using hotjob.Models.Job;
using hotjob.Services.Common;
using hotjob.Services.Job;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>Skill-based job search for a resume owned by the caller.</summary>
[ApiController]
[ApiValidationEnvelope]
[Route("api/jobs")]
[Authorize]
public sealed class JobSearchController(IJobService jobService) : ApiControllerBase
{
    /// <summary>Lists published jobs that share skills with the caller's resume.</summary>
    /// <response code="200">Page of matching jobs, matched skills, and stored match percentages.</response>
    /// <response code="400">Invalid query parameters.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="404">Resume not found or not owned by the caller.</response>
    [HttpGet("matching-resume/{resumeId:int}")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<SkillMatchJobListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MatchResume(
        int resumeId,
        [FromQuery] string? city,
        [FromQuery] JobType? jobType,
        [FromQuery] int minSkillMatches = 1,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
        {
            return FromError(ServiceResult.Fail(
                ServiceErrorType.Unauthorized, "Not authenticated."));
        }

        return FromResult(await jobService.GetJobsMatchingResumeAsync(
            userId,
            resumeId,
            city,
            jobType,
            minSkillMatches,
            page,
            pageSize,
            cancellationToken));
    }
}
