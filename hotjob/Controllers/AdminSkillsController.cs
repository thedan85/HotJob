using HotJob.Dtos;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Services.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>Admin skill catalogue CRUD.</summary>
[ApiController]
[ApiValidationEnvelope]
[Route("api/admin/skills")]
[Authorize(Roles = "Admin", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AdminSkillsController(IAdminService adminService) : ApiControllerBase
{
    /// <summary>Lists skills with optional name search and exact category filter.</summary>
    /// <response code="200">Page of skills.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Not an admin.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AdminSkillDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        FromResult(await adminService.GetSkillsAsync(
            search, category, page, pageSize, cancellationToken));

    /// <summary>Gets one skill with resume and job usage counts.</summary>
    /// <response code="200">Skill detail.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Not an admin.</response>
    /// <response code="404">Skill not found.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<AdminSkillDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int id,
        CancellationToken cancellationToken = default) =>
        FromResult(await adminService.GetSkillAsync(id, cancellationToken));

    /// <summary>Creates a skill. Name is trimmed, case-insensitive unique, 1-100 characters.</summary>
    /// <response code="201">Created skill.</response>
    /// <response code="400">Validation errors.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Not an admin.</response>
    /// <response code="409">Skill name already exists.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdminSkillDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSkillRequest request,
        CancellationToken cancellationToken = default) =>
        FromResultCreated(await adminService.CreateSkillAsync(request, cancellationToken));

    /// <summary>Updates a skill. Existing resume and job links stay valid.</summary>
    /// <response code="200">Updated skill.</response>
    /// <response code="400">Validation errors.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Not an admin.</response>
    /// <response code="404">Skill not found.</response>
    /// <response code="409">New name conflicts with an existing skill.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<AdminSkillDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateSkillRequest request,
        CancellationToken cancellationToken = default) =>
        FromResult(await adminService.UpdateSkillAsync(id, request, cancellationToken));

    /// <summary>Deletes an unused skill. A skill referenced by a resume or job returns 409.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Not an admin.</response>
    /// <response code="404">Skill not found.</response>
    /// <response code="409">Skill is in use.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await adminService.DeleteSkillAsync(id, cancellationToken);
        return result.Success ? NoContent() : FromError(result);
    }
}
