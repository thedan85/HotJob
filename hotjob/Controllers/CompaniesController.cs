using HotJob.Dtos;
using hotjob.DTOs.Company;
using hotjob.DTOs.Job;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using hotjob.Services.Company;

namespace hotjob.Controllers;

/// <summary>Public company directory: approved companies with name search + industry/location filters.</summary>
[ApiController]
[Route("api/companies")]
[AllowAnonymous]
public sealed class CompaniesController(ICompanyService companyService) : ApiControllerBase
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

        return FromResult(result);
    }

    /// <summary>Distinct industry/location options from approved companies (for filter dropdown).</summary>
    /// <response code="200">Filter options.</response>
    [HttpGet("filters")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CompanyFilterOptionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Filters(CancellationToken cancellationToken)
    {
        var result = await companyService.GetFilterOptionsAsync(cancellationToken);
        return FromResult(result);
    }
}
