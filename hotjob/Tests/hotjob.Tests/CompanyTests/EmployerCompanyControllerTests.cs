using System.Security.Claims;
using hotjob.Controllers;
using hotjob.DTOs.Employer;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests;

public sealed class EmployerCompanyControllerTests
{
    [Fact]
    public async Task GetPage_MapsNotFound()
    {
        var service = new StubCompanyService
        {
            PageResult = ServiceResult<CompanyPageResponse>.Fail(ServiceErrorType.NotFound, "missing")
        };
        var controller = CreateController(service);

        var result = await controller.GetPage(CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Update_MapsValidationToBadRequest()
    {
        var service = new StubCompanyService
        {
            DetailResult = ServiceResult<CompanyDetailResponse>.Fail(ServiceErrorType.Validation, "bad url")
        };
        var controller = CreateController(service);

        var result = await controller.Update(new UpdateCompanyRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Approve_MapsConflict()
    {
        var service = new StubCompanyService
        {
            MemberResult = ServiceResult<CompanyMemberDto>.Fail(
                ServiceErrorType.Conflict, "Only pending members can be approved/rejected.")
        };
        var controller = CreateController(service);

        var result = await controller.Approve(3, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Reject_Success_ReturnsOkPayload()
    {
        var member = new CompanyMemberDto { Id = 4, FullName = "HR" };
        var service = new StubCompanyService
        {
            MemberResult = ServiceResult<CompanyMemberDto>.Ok(member)
        };
        var controller = CreateController(service);

        var result = await controller.Reject(4, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
    }

    private static EmployerCompanyController CreateController(StubCompanyService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                        "test"))
                }
            }
        };

    private sealed class StubCompanyService : ICompanyService
    {
        public ServiceResult<CompanyPageResponse> PageResult { get; init; } =
            ServiceResult<CompanyPageResponse>.Fail(ServiceErrorType.NotFound);
        public ServiceResult<CompanyDetailResponse> DetailResult { get; init; } =
            ServiceResult<CompanyDetailResponse>.Fail(ServiceErrorType.Unauthorized);
        public ServiceResult<CompanyMemberDto> MemberResult { get; init; } =
            ServiceResult<CompanyMemberDto>.Fail(ServiceErrorType.NotFound);

        public Task<ServiceResult<CompanyPageResponse>> GetPageAsync(
            string? userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult);

        public Task<ServiceResult<CompanyDetailResponse>> UpdateAsync(
            string? userId, UpdateCompanyRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(DetailResult);

        public Task<ServiceResult<CompanyMemberDto>> ApproveMemberAsync(
            string? userId, int memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MemberResult);

        public Task<ServiceResult<CompanyMemberDto>> RejectMemberAsync(
            string? userId, int memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MemberResult);
    }
}
