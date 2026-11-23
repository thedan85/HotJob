using HotJob.Dtos;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers.Admin;

/// <summary>Lightweight probe to confirm the caller holds an admin token.</summary>
[ApiController]
public sealed class AdminStatusController : AdminControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public IActionResult Status() => FromResult(ServiceResult<string>.Ok("Admin access granted."));
}
