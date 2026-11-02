using System.Reflection;
using HotJob.Dtos;
using hotjob.Controllers;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests.AdminTests;

public sealed class AdminSkillsControllerTests
{
    [Fact]
    public async Task List_Success_ReturnsOkEnvelope()
    {
        var service = new StubAdminService
        {
            PageResult = ServiceResult<PagedResponse<AdminSkillDto>>.Ok(
                new PagedResponse<AdminSkillDto>
                {
                    Items = new[] { new AdminSkillDto(1, "C#", "Backend", 2, 3) },
                    TotalCount = 1,
                    Page = 1,
                    PageSize = 50
                })
        };
        var controller = new AdminSkillsController(service);

        var result = await controller.List(null, null, 1, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ApiResponse<PagedResponse<AdminSkillDto>>>(ok.Value);
        Assert.True(body.Success);
        Assert.Single(body.Data!.Items);
    }

    [Fact]
    public async Task Create_Success_ReturnsCreated()
    {
        var service = new StubAdminService
        {
            CreateResult = ServiceResult<AdminSkillDto>.Ok(
                new AdminSkillDto(4, "Rust", "Backend", 0, 0))
        };
        var controller = new AdminSkillsController(service);

        var result = await controller.Create(
            new CreateSkillRequest { Name = "Rust", Category = "Backend" },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Delete_Conflict_Returns409()
    {
        var service = new StubAdminService
        {
            DeleteResult = ServiceResult.Fail(ServiceErrorType.Conflict, "in use")
        };
        var controller = new AdminSkillsController(service);

        var result = await controller.Delete(1, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Delete_Success_Returns204()
    {
        var controller = new AdminSkillsController(new StubAdminService
        {
            DeleteResult = ServiceResult.Ok()
        });

        var result = await controller.Delete(1, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Get_Missing_Returns404()
    {
        var service = new StubAdminService
        {
            DetailResult = ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.NotFound, "missing")
        };
        var controller = new AdminSkillsController(service);

        var result = await controller.Get(999, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void Controller_RequiresAdminJwt()
    {
        var authorize = typeof(AdminSkillsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authorize.AuthenticationSchemes);
    }

    private sealed class StubAdminService : IAdminService
    {
        public ServiceResult<PagedResponse<AdminSkillDto>> PageResult { get; init; } = null!;
        public ServiceResult<AdminSkillDto> DetailResult { get; init; } = null!;
        public ServiceResult<AdminSkillDto> CreateResult { get; init; } = null!;
        public ServiceResult<AdminSkillDto> UpdateResult { get; init; } = null!;
        public ServiceResult DeleteResult { get; init; } = null!;

        public Task<ServiceResult<AdminDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<IReadOnlyList<AdminUserListItemDto>>> GetUsersAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<bool>> ToggleUserActiveAsync(string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<IReadOnlyList<AdminCompanyListItemDto>>> GetCompaniesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<bool>> UpdateCompanyStatusAsync(int companyId, CompanyStatus status, string? rejectionReason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<IReadOnlyList<AdminJobListItemDto>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<bool>> DeleteJobAsync(int jobId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<PagedResponse<AdminSkillDto>>> GetSkillsAsync(string? search, string? category, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult);

        public Task<ServiceResult<AdminSkillDto>> GetSkillAsync(int skillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DetailResult);

        public Task<ServiceResult<AdminSkillDto>> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult);

        public Task<ServiceResult<AdminSkillDto>> UpdateSkillAsync(int skillId, UpdateSkillRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(UpdateResult);

        public Task<ServiceResult> DeleteSkillAsync(int skillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeleteResult);
    }
}
