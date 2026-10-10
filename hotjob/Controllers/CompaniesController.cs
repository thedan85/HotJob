using HotJob.Dtos;
using hotjob.DTOs.Company;
using hotjob.DTOs.Job;
using hotjob.Services.Common;
using hotjob.Services.Company;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>Public company directory: approved companies with name search + industry/location filters.</summary>
[ApiController]
[Route("api/companies")]
[AllowAnonymous]
public sealed class CompaniesController(ICompanyService companyService) : ControllerBase
{
    /// <summary>List approved companies (name search, multi-select industry/location filters; fixed 20 per page).</summary>
    /// <response code="200">Paged company list.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<CompanyListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] List<string>? industries,
        [FromQuery] List<string>? locations,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await companyService.ListAsync(new CompanyListQuery
        {
            Search = search,
            Industries = industries ?? [],
            Locations = locations ?? [],
            Page = page
        }, cancellationToken);

        return result.Success ? Ok(ToResponse(result.Data!)) : MapError(result);
    }

    /// <summary>Distinct industry/location options from approved companies (for filter dropdown).</summary>
    /// <response code="200">Filter options.</response>
    [HttpGet("filters")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CompanyFilterOptionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Filters(CancellationToken cancellationToken)
    {
        var result = await companyService.GetFilterOptionsAsync(cancellationToken);
        return result.Success ? Ok(ToResponse(result.Data!)) : MapError(result);
    }

    private static ApiResponse<T> ToResponse<T>(T data) => new() { Success = true, Data = data };

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
