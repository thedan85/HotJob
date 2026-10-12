using System.Security.Claims;
using HotJob.Dtos;
using hotjob.DTOs.Employer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using hotjob.Services.Employer;

namespace hotjob.Controllers;

/// <summary>Interview management for own-company applications (2.3.4).</summary>
[ApiController]
[Route("api/employer/interviews")]
[Authorize(Roles = "Employer", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class EmployerInterviewsController(IInterviewService interviewService) : ApiControllerBase
{
    /// <summary>Reschedule / edit notes / change status of an interview.</summary>
    /// <response code="200">Updated interview.</response>
    /// <response code="400">Past date or bad enum value.</response>
    /// <response code="401">Not an active employer.</response>
    /// <response code="404">Interview not found in own company.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<InterviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await interviewService.UpdateAsync(CurrentUserId(), id, request, cancellationToken);
        return FromResult(result);
    }

    /// <summary>Cancel an interview (soft-cancel: Status=Cancelled, row kept).</summary>
    /// <response code="200">Cancelled interview.</response>
    /// <response code="401">Not an active employer.</response>
    /// <response code="404">Interview not found in own company.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<InterviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        var result = await interviewService.CancelAsync(CurrentUserId(), id, cancellationToken);
        return FromResult(result);
    }

    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
}
