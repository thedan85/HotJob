using System.Security.Claims;
using HotJob.Dtos;
using hotjob.DTOs.Employer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using hotjob.Services.Employer;

namespace hotjob.Controllers;

/// <summary>Company profile + team member management for the caller's own company.</summary>
[ApiController]
[Route("api/employer/company")]
[Authorize(Roles = "Employer", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class EmployerCompanyController(ICompanyService companyService) : ApiControllerBase
{
    /// <summary>Company profile with members and the caller's owner flag.</summary>
    /// <response code="200">Company page.</response>
    /// <response code="401">Not an active employer.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CompanyPageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPage(CancellationToken cancellationToken)
    {
        var result = await companyService.GetPageAsync(CurrentUserId(), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Update own-company profile fields (owner only).</summary>
    /// <response code="200">Updated company.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="401">Not the company owner.</response>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<CompanyDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await companyService.UpdateAsync(CurrentUserId(), request, cancellationToken);
        return FromResult(result);
    }

    /// <summary>Approve a pending join request (owner only).</summary>
    /// <response code="200">Approved member.</response>
    /// <response code="401">Not the company owner.</response>
    /// <response code="404">Member not in own company.</response>
    /// <response code="409">Member is not pending (or is self/owner).</response>
    [HttpPost("members/{id:int}/approve")]
    [ProducesResponseType(typeof(ApiResponse<CompanyMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        var result = await companyService.ApproveMemberAsync(CurrentUserId(), id, cancellationToken);
        return FromResult(result);
    }

    /// <summary>Reject a pending join request (owner only).</summary>
    /// <response code="200">Rejected member.</response>
    /// <response code="401">Not the company owner.</response>
    /// <response code="404">Member not in own company.</response>
    /// <response code="409">Member is not pending (or is self/owner).</response>
    [HttpPost("members/{id:int}/reject")]
    [ProducesResponseType(typeof(ApiResponse<CompanyMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(int id, CancellationToken cancellationToken)
    {
        var result = await companyService.RejectMemberAsync(CurrentUserId(), id, cancellationToken);
        return FromResult(result);
    }

    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
}
