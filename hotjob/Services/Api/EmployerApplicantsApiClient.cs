using hotjob.DTOs.Employer;

namespace hotjob.Services.Api;

/// <summary>Typed client for the employer applicant/ATS controller (<c>api/employer/applications</c>).</summary>
public sealed class EmployerApplicantsApiClient(
    IHttpClientFactory httpClientFactory,
    hotjob.Services.Authentication.AuthSession session,
    hotjob.Services.Authentication.HotJobAuthStateProvider authStateProvider,
    Microsoft.AspNetCore.Components.NavigationManager navigationManager,
    Microsoft.JSInterop.IJSRuntime js)
    : ApiClientBase(httpClientFactory, session, authStateProvider, navigationManager, js)
{
    public Task<ApiResult<PagedResult<ApplicantListItemResponse>>> ListAsync(
        ApplicantListQuery query, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<ApplicantListItemResponse>>(
            Query("api/employer/applications",
                ("jobPostingId", query.JobPostingId?.ToString()),
                ("status", query.Status?.ToString()),
                ("search", query.Search),
                ("page", query.Page.ToString()),
                ("pageSize", query.PageSize.ToString())),
            cancellationToken);

    public Task<ApiResult<ApplicantDetailResponse>> GetDetailAsync(
        int applicationId, CancellationToken cancellationToken = default) =>
        GetAsync<ApplicantDetailResponse>($"api/employer/applications/{applicationId}", cancellationToken);

    public Task<ApiResult<ApplicantDetailResponse>> UpdateStatusAsync(
        int applicationId, UpdateStatusRequest request, CancellationToken cancellationToken = default) =>
        PatchAsync<UpdateStatusRequest, ApplicantDetailResponse>(
            $"api/employer/applications/{applicationId}/status", request, cancellationToken);

    public Task<ApiResult<NotificationResponse>> NotifyAsync(
        int applicationId, SendNotificationRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<SendNotificationRequest, NotificationResponse>(
            $"api/employer/applications/{applicationId}/notify", request, cancellationToken);

    public Task<ApiResult<InterviewResponse>> CreateInterviewAsync(
        int applicationId, CreateInterviewRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<CreateInterviewRequest, InterviewResponse>(
            $"api/employer/applications/{applicationId}/interviews", request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<InterviewResponse>>> ListInterviewsAsync(
        int applicationId, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<InterviewResponse>>(
            $"api/employer/applications/{applicationId}/interviews", cancellationToken);
}
