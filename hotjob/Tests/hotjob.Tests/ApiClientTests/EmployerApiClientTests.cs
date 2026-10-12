using System.Net;
using hotjob.DTOs.Employer;
using hotjob.DTOs.Job;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Tests.ApiClients;

namespace hotjob.Tests;

public sealed class EmployerJobsApiClientTests
{
    [Fact]
    public async Task List_BuildsQuery_AttachesBearer()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(new PagedResponse<JobListItemResponse>
        {
            Items = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 10
        }));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Jobs().ListAsync(JobStatus.Published, "dev", 2, 10);

        Assert.True(result.Success);
        var request = scope.Handler.Requests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/employer/jobs", request.RequestUri!.AbsolutePath);
        Assert.Contains("status=Published", request.RequestUri.Query);
        Assert.Contains("search=dev", request.RequestUri.Query);
        Assert.Contains("page=2", request.RequestUri.Query);
    }

    [Fact]
    public async Task Create_PostsBody_ReturnsDetail()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.Created, ApiStubs.Envelope(
            new JobDetailResponse(1, 1, "Dev", "d", null, null, 0, JobType.FullTime, null,
                JobStatus.Draft, "Co", [], [], 0, DateTime.UtcNow)));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Jobs().CreateAsync(new CreateJobRequest { Title = "Dev", Description = "d" });

        Assert.True(result.Success);
        Assert.Equal("Dev", result.Data!.Title);
        Assert.Equal(HttpMethod.Post, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/employer/jobs", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Update_PutsToId()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(
            new JobDetailResponse(7, 1, "Dev", "d", null, null, 0, JobType.FullTime, null,
                JobStatus.Published, "Co", [], [], 0, DateTime.UtcNow)));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Jobs().UpdateAsync(7, new UpdateJobRequest { Title = "Dev", Description = "d" });

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/employer/jobs/7", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DeleteOrClose_DeletesId_PassesErrorsThrough()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.NotFound,
            new HotJob.Dtos.ApiResponse<JobDeleteResponse> { Success = false, Errors = ["Job not found."] });
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Jobs().DeleteOrCloseAsync(9);

        Assert.False(result.Success);
        Assert.Equal(HttpStatusCode.NotFound, result.Status);
        Assert.Equal(["Job not found."], result.Errors);
        Assert.Equal(HttpMethod.Delete, scope.Handler.Requests[0].Method);
    }

    [Fact]
    public async Task Get_FetchesDetail()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(
            new JobDetailResponse(3, 1, "Dev", "d", null, null, 0, JobType.FullTime, null,
                JobStatus.Draft, "Co", [], [], 0, DateTime.UtcNow)));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Jobs().GetAsync(3);

        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.Id);
        Assert.Equal("/api/employer/jobs/3", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }
}

public sealed class EmployerApplicantsApiClientTests
{
    [Fact]
    public async Task List_BuildsQuery()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(new PagedResult<ApplicantListItemResponse>
        {
            Items = [],
            Page = 1,
            PageSize = 15,
            TotalCount = 0,
            TotalPages = 0
        }));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Applicants().ListAsync(new ApplicantListQuery
        {
            JobPostingId = 5,
            Status = ApplicationStatus.Viewed,
            Search = "an",
            Page = 1,
            PageSize = 15
        });

        Assert.True(result.Success);
        var request = scope.Handler.Requests[0];
        Assert.Equal("/api/employer/applications", request.RequestUri!.AbsolutePath);
        Assert.Contains("jobPostingId=5", request.RequestUri.Query);
        Assert.Contains("status=Viewed", request.RequestUri.Query);
        Assert.Contains("search=an", request.RequestUri.Query);
    }

    [Fact]
    public async Task UpdateStatus_PatchesStatusEndpoint()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(new ApplicantDetailResponse
        {
            ApplicationId = 4,
            CurrentStatus = ApplicationStatus.Viewed
        }));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Applicants().UpdateStatusAsync(4, new UpdateStatusRequest { Status = ApplicationStatus.Viewed });

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Patch, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/employer/applications/4/status", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Notify_PostsToNotifyEndpoint()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(
            new NotificationResponse(1, "u", "Hi", null, false, DateTime.UtcNow)));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Applicants().NotifyAsync(4, new SendNotificationRequest { Title = "Hi" });

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/employer/applications/4/notify", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CreateInterview_PostsToInterviewsEndpoint()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(
            new InterviewResponse(1, 4, DateTime.UtcNow.AddDays(1), null, null, null, InterviewStatus.Scheduled)));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Applicants().CreateInterviewAsync(4,
            new CreateInterviewRequest { InterviewDate = DateTime.UtcNow.AddDays(1) });

        Assert.True(result.Success);
        Assert.Equal("/api/employer/applications/4/interviews", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetDetail_AndListInterviews_HitOwnRoutes()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(new ApplicantDetailResponse { ApplicationId = 4 }));
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope<IReadOnlyList<InterviewResponse>>([]));
        var scope = ApiStubs.Scope(handler);

        var detail = await scope.Applicants().GetDetailAsync(4);
        var interviews = await scope.Applicants().ListInterviewsAsync(4);

        Assert.True(detail.Success);
        Assert.True(interviews.Success);
        Assert.Equal("/api/employer/applications/4", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/api/employer/applications/4/interviews", scope.Handler.Requests[1].RequestUri!.AbsolutePath);
    }
}

public sealed class EmployerInterviewsApiClientTests
{
    [Fact]
    public async Task Update_PutsToInterviewId()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(
            new InterviewResponse(2, 4, DateTime.UtcNow, null, null, null, InterviewStatus.Completed)));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Interviews().UpdateAsync(2, new UpdateInterviewRequest { Status = InterviewStatus.Completed });

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/employer/interviews/2", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Cancel_DeletesInterviewId()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(
            new InterviewResponse(2, 4, DateTime.UtcNow, null, null, null, InterviewStatus.Cancelled)));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Interviews().CancelAsync(2);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/employer/interviews/2", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }
}
