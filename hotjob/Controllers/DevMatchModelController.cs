using System.Security.Claims;
using HotJob.Dtos;
using hotjob.DTOs.ML;
using hotjob.Services.ML;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>
/// Development-only trigger for the synthetic match-model demo. It disappears
/// outside Development or unless <c>DevTools:SyntheticTrainingEnabled</c> is true.
/// </summary>
[ApiController]
[Route("api/dev/match-model")]
[Authorize(Roles = "Admin", AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DevMatchModelController(
    ISyntheticMatchTrainingService syntheticMatchTrainingService,
    IWebHostEnvironment environment,
    IConfiguration configuration) : ApiControllerBase
{
    /// <summary>Configuration key that must opt in to synthetic training.</summary>
    public const string EnabledKey = "DevTools:SyntheticTrainingEnabled";

    /// <summary>Seeds 14 synthetic candidates against 4 published jobs, then trains and promotes the match model.</summary>
    /// <response code="200">Seed counts and the training result.</response>
    /// <response code="400">Fewer than 4 published jobs with skills, or training/seed validation failed.</response>
    /// <response code="401">Not an admin.</response>
    /// <response code="404">Not Development, or the feature flag is off.</response>
    [HttpPost("seed-and-train")]
    [ProducesResponseType(typeof(ApiResponse<SyntheticMatchTrainingResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SeedAndTrain(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>(EnabledKey))
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var result = await syntheticMatchTrainingService.SeedAndTrainAsync(userId, cancellationToken);
        return FromResult(result);
    }
}
