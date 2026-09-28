using System.Security.Claims;
using HotJob.Dtos;
using hotjob.DTOs.Employer;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>Employer ATS inbox (2.3): list/view applicants, change status, notify, manage interviews. Requires Employer role, active membership; every query scoped to own company.</summary>
[ApiController]
[Route("api/employer/applications")]
[Authorize(Roles = "Employer", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class EmployerApplicationsController(
    IApplicantService applicantService,
    IInterviewService interviewService,
    INotificationService notificationService) : ControllerBase
{
    /// <summary>2.3.1 List applicants to own-company jobs (filter by job, status, name/email search; paginated).</summary>
    /// <response code="200">Paged applicant list.</response>
    /// <response code="401">Not an active employer.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ApplicantListItemResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] ApplicantListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await applicantService.ListAsync(CurrentUserId(), query, cancellationToken);
        return result.Success ? Ok(ToResponse(result.Data!)) : MapError(result);
    }

    /// <summary>2.3.2 View one application with resume snapshot, full resume (education/experience/skills), status history, interviews.</summary>
    /// <response code="200">Application detail.</response>
    /// <response code="401">Not an active employer.</response>
    /// <response code="404">Application not found in own company.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ApplicantDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken)
    {
        var result = await applicantService.GetDetailAsync(CurrentUserId(), id, cancellationToken);
        return result.Success ? Ok(ToResponse(result.Data!)) : MapError(result);
    }

    /// <summary>2.3.3 Approve/reject a resume: forward-only transition (Pending→Viewed→Shortlisted→Interviewing→Offered, Rejected from any open state). Writes history + notifies candidate.</summary>
    /// <response code="200">Updated application detail.</response>
    /// <response code="400">Employer cannot set Withdrawn.</response>
    /// <response code="401">Not an active employer.</response>
    /// <response code="404">Application not found in own company.</response>
    /// <response code="409">Illegal transition (e.g. from a terminal state).</response>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(ApiResponse<ApplicantDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        int id,
        [FromBody] UpdateStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await applicantService.UpdateStatusAsync(CurrentUserId(), id, request, cancellationToken);
        return result.Success ? Ok(ToResponse(result.Data!)) : MapError(result);
    }

    /// <summary>2.3.5 Send a manual notification to the candidate of this application.</summary>
    /// <response code="200">Created notification.</response>
    /// <response code="400">Title required (max 200).</response>
    /// <response code="401">Not an active employer.</response>
    /// <response code="404">Application not found in own company.</response>
    [HttpPost("{id:int}/notify")]
    [ProducesResponseType(typeof(ApiResponse<NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Notify(
        int id,
        [FromBody] SendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await notificationService.NotifyAsync(CurrentUserId(), id, request, cancellationToken);
        return result.Success ? Ok(ToResponse(result.Data!)) : MapError(result);
    }

    /// <summary>2.3.4 Schedule an interview (date must be future). Auto-advances Shortlisted→Interviewing + notifies candidate.</summary>
    /// <response code="200">Created interview.</response>
    /// <response code="400">Past date or bad interview type.</response>
    /// <response code="401">Not an active employer.</response>
    /// <response code="404">Application not found in own company.</response>
    /// <response code="409">Application already terminal (Offered/Rejected/Withdrawn).</response>
    [HttpPost("{id:int}/interviews")]
    [ProducesResponseType(typeof(ApiResponse<InterviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInterview(
        int id,
        [FromBody] CreateInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await interviewService.CreateAsync(CurrentUserId(), id, request, cancellationToken);
        return result.Success ? Ok(ToResponse(result.Data!)) : MapError(result);
    }

    /// <summary>List interviews of one application (ordered by date).</summary>
    /// <response code="200">Interview list.</response>
    /// <response code="401">Not an active employer.</response>
    /// <response code="404">Application not found in own company.</response>
    [HttpGet("{id:int}/interviews")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<InterviewResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListInterviews(int id, CancellationToken cancellationToken)
    {
        var result = await interviewService.ListAsync(CurrentUserId(), id, cancellationToken);
        return result.Success ? Ok(ToResponse(result.Data!)) : MapError(result);
    }

    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private static ApiResponse<T> ToResponse<T>(T data) => new() { Success = true, Data = data };

    private static IActionResult MapError(ServiceResult result) =>
        result.ErrorType switch
        {
            ServiceErrorType.Conflict => new ConflictObjectResult(new ApiResponse<object> { Success = false, Errors = result.Errors.ToList() }),
            ServiceErrorType.NotFound => new NotFoundObjectResult(new ApiResponse<object> { Success = false, Errors = result.Errors.ToList() }),
            ServiceErrorType.Unauthorized => new UnauthorizedObjectResult(new ApiResponse<object> { Success = false, Errors = result.Errors.ToList() }),
            _ => new BadRequestObjectResult(new ApiResponse<object> { Success = false, Errors = result.Errors.ToList() })
        };
}
