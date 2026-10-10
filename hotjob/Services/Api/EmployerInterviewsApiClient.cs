using hotjob.DTOs.Employer;

namespace hotjob.Services.Api;

/// <summary>Typed client for the employer interviews controller (<c>api/employer/interviews</c>).</summary>
public sealed class EmployerInterviewsApiClient(
    IHttpClientFactory httpClientFactory,
    hotjob.Services.Authentication.AuthSession session,
    hotjob.Services.Authentication.HotJobAuthStateProvider authStateProvider,
    Microsoft.AspNetCore.Components.NavigationManager navigationManager,
    Microsoft.JSInterop.IJSRuntime js)
    : ApiClientBase(httpClientFactory, session, authStateProvider, navigationManager, js)
{
    public Task<ApiResult<InterviewResponse>> UpdateAsync(
        int interviewId, UpdateInterviewRequest request, CancellationToken cancellationToken = default) =>
        PutAsync<UpdateInterviewRequest, InterviewResponse>(
            $"api/employer/interviews/{interviewId}", request, cancellationToken);

    public Task<ApiResult<InterviewResponse>> CancelAsync(
        int interviewId, CancellationToken cancellationToken = default) =>
        DeleteAsync<InterviewResponse>($"api/employer/interviews/{interviewId}", cancellationToken);
}
