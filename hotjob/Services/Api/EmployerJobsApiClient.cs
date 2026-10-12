using hotjob.DTOs.Job;
using hotjob.Models.Job;

namespace hotjob.Services.Api;

/// <summary>Typed client for the employer jobs controller (<c>api/employer/jobs</c>).</summary>
public sealed class EmployerJobsApiClient(
    IHttpClientFactory httpClientFactory,
    hotjob.Services.Authentication.AuthSession session,
    hotjob.Services.Authentication.HotJobAuthStateProvider authStateProvider,
    Microsoft.AspNetCore.Components.NavigationManager navigationManager,
    Microsoft.JSInterop.IJSRuntime js)
    : ApiClientBase(httpClientFactory, session, authStateProvider, navigationManager, js)
{
    public Task<ApiResult<PagedResponse<JobListItemResponse>>> ListAsync(
        JobStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResponse<JobListItemResponse>>(
            Query("api/employer/jobs",
                ("status", status?.ToString()),
                ("search", search),
                ("page", page.ToString()),
                ("pageSize", pageSize.ToString())),
            cancellationToken);

    public Task<ApiResult<JobDetailResponse>> GetAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<JobDetailResponse>($"api/employer/jobs/{id}", cancellationToken);

    public Task<ApiResult<JobDetailResponse>> CreateAsync(
        CreateJobRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CreateJobRequest, JobDetailResponse>("api/employer/jobs", request, cancellationToken);

    public Task<ApiResult<JobDetailResponse>> UpdateAsync(
        int id, UpdateJobRequest request, CancellationToken cancellationToken = default) =>
        PutAsync<UpdateJobRequest, JobDetailResponse>($"api/employer/jobs/{id}", request, cancellationToken);

    public Task<ApiResult<JobDeleteResponse>> DeleteOrCloseAsync(
        int id, CancellationToken cancellationToken = default) =>
        DeleteAsync<JobDeleteResponse>($"api/employer/jobs/{id}", cancellationToken);
}
