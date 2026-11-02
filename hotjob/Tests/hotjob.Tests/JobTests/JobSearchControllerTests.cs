using System.Security.Claims;
using HotJob.Dtos;
using hotjob.Controllers;
using hotjob.DTOs.Job;
using hotjob.Models.Job;
using hotjob.Services.Common;
using hotjob.Services.Job;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests.JobTests;

public sealed class JobSearchControllerTests
{
    [Fact]
    public async Task MatchResume_Success_ReturnsOkEnvelope()
    {
        var service = new StubJobService
        {
            MatchResult = ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Ok(
                new PagedResponse<SkillMatchJobListItemDto>
                {
                    Items = Array.Empty<SkillMatchJobListItemDto>(),
                    TotalCount = 0,
                    Page = 1,
                    PageSize = 20
                })
        };
        var controller = CreateController(service);

        var result = await controller.MatchResume(1, null, null, 1, 1, 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ApiResponse<PagedResponse<SkillMatchJobListItemDto>>>(ok.Value);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.Equal("user-1", service.MatchUserId);
    }

    [Fact]
    public async Task MatchResume_MissingUser_Returns401()
    {
        var service = new StubJobService();
        var controller = new JobSearchController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.MatchResume(1, null, null, 1, 1, 20, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var body = Assert.IsType<ApiResponse<object>>(unauthorized.Value);
        Assert.False(body.Success);
        Assert.Null(service.MatchUserId);
    }

    [Fact]
    public async Task MatchResume_NotFound_Returns404()
    {
        var service = new StubJobService
        {
            MatchResult = ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Fail(
                ServiceErrorType.NotFound,
                "Resume not found.")
        };
        var controller = CreateController(service);

        var result = await controller.MatchResume(99, null, null, 1, 1, 20, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var body = Assert.IsType<ApiResponse<object>>(notFound.Value);
        Assert.False(body.Success);
        Assert.Equal(new[] { "Resume not found." }, body.Errors);
    }

    private static JobSearchController CreateController(IJobService service) =>
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

    private sealed class StubJobService : IJobService
    {
        public ServiceResult<PagedResponse<SkillMatchJobListItemDto>> MatchResult { get; init; } =
            ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Fail(ServiceErrorType.NotFound);

        public string? MatchUserId { get; private set; }

        public Task<ServiceResult<PagedResponse<SkillMatchJobListItemDto>>> GetJobsMatchingResumeAsync(
            string userId, int resumeId, string? city, JobType? jobType,
            int minSkillMatches, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            MatchUserId = userId;
            return Task.FromResult(MatchResult);
        }

        public Task<ServiceResult<JobDetailResponse>> CreateAsync(
            string userId, CreateJobRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<JobDetailResponse>> UpdateAsync(
            string userId, int id, UpdateJobRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<JobDeleteResponse>> DeleteOrCloseAsync(
            string userId, int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<JobDeleteResponse>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<JobDetailResponse>> AssignCategoriesAsync(
            string userId, int id, AssignCategoriesRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<JobDetailResponse>> AssignSkillsAsync(
            string userId, int id, AssignSkillsRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<PagedResponse<JobListItemResponse>>> ListAsync(
            string userId, JobStatus? status, string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<PagedResponse<JobListItemResponse>>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<JobDetailResponse>> GetAsync(
            string userId, int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<PagedResponse<PublicJobListItemResponse>>> BrowsePublishedAsync(
            string? keyword, string? city, string? skill, int? companyId,
            JobType? jobType, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<PagedResponse<PublicJobListItemResponse>>.Fail(ServiceErrorType.NotFound));

        public Task<ServiceResult<JobDetailResponse>> GetPublishedAsync(
            int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound));
    }
}
