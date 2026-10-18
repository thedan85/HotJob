using System.Reflection;
using HotJob.Dtos;
using hotjob.Controllers;
using hotjob.DTOs.Company;
using hotjob.DTOs.Job;
using hotjob.Services.Common;
using hotjob.Services.Company;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests;

public sealed class CompaniesControllerTests
{
    private sealed class StubCompanyService : ICompanyService
    {
        public CompanyListQuery? SeenQuery;
        public bool FiltersCalled;

        public Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
            CompanyListQuery query, CancellationToken cancellationToken = default)
        {
            SeenQuery = query;
            return Task.FromResult(ServiceResult<PagedResponse<CompanyListItemDto>>.Ok(
                new PagedResponse<CompanyListItemDto>
                {
                    Items = [new CompanyListItemDto(1, "Acme", "Software", "Hanoi", null, null, null)],
                    TotalCount = 1,
                    Page = 1,
                    PageSize = 20
                }));
        }

        public Task<ServiceResult<CompanyFilterOptionsDto>> GetFilterOptionsAsync(
            CancellationToken cancellationToken = default)
        {
            FiltersCalled = true;
            return Task.FromResult(ServiceResult<CompanyFilterOptionsDto>.Ok(
                new CompanyFilterOptionsDto(["Software"], ["Hanoi"])));
        }

        public Task<ServiceResult<CompanyDetailDto>> GetDetailAsync(
            int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult<CompanyDetailDto>.Ok(
                new CompanyDetailDto(id, "Acme", "Software", "Hanoi", null, null, null, null, null)));
        }
    }

    [Fact]
    public async Task List_NoAuthClaim_Returns200Envelope()
    {
        var stub = new StubCompanyService();
        var controller = new CompaniesController(stub)
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        var action = await controller.List("acme", ["Software"], ["Hanoi"], 2);

        var ok = Assert.IsType<OkObjectResult>(action);
        var body = Assert.IsType<ApiResponse<PagedResponse<CompanyListItemDto>>>(ok.Value);
        Assert.True(body.Success);
        Assert.Equal("Acme", body.Data!.Items[0].CompanyName);
        Assert.Equal("acme", stub.SeenQuery!.Search);
        Assert.Equal(2, stub.SeenQuery.Page);
    }

    [Fact]
    public void Controller_AllowsAnonymous()
    {
        var type = typeof(CompaniesController);
        Assert.NotNull(type.GetCustomAttribute<AllowAnonymousAttribute>());
        foreach (var method in new[] { nameof(CompaniesController.List), nameof(CompaniesController.Filters) })
            Assert.NotNull(type.GetMethod(method)!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(type.GetCustomAttribute<AuthorizeAttribute>());
    }
}
