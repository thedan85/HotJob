using hotjob.DTOs.Employer;

namespace hotjob.Services.Api;

/// <summary>Typed client for the employer company controller (<c>api/employer/company</c>).</summary>
public sealed class CompanyApiClient(
    IHttpClientFactory httpClientFactory,
    hotjob.Services.Authentication.AuthSession session,
    hotjob.Services.Authentication.HotJobAuthStateProvider authStateProvider,
    Microsoft.AspNetCore.Components.NavigationManager navigationManager,
    Microsoft.JSInterop.IJSRuntime js)
    : ApiClientBase(httpClientFactory, session, authStateProvider, navigationManager, js)
{
    public Task<ApiResult<CompanyPageResponse>> GetPageAsync(CancellationToken cancellationToken = default) =>
        GetAsync<CompanyPageResponse>("api/employer/company", cancellationToken);

    public Task<ApiResult<CompanyDetailResponse>> UpdateAsync(
        UpdateCompanyRequest request, CancellationToken cancellationToken = default) =>
        PutAsync<UpdateCompanyRequest, CompanyDetailResponse>("api/employer/company", request, cancellationToken);

    public Task<ApiResult<CompanyMemberDto>> ApproveMemberAsync(
        int memberId, CancellationToken cancellationToken = default) =>
        PostAsync<object, CompanyMemberDto>(
            $"api/employer/company/members/{memberId}/approve", new { }, cancellationToken);

    public Task<ApiResult<CompanyMemberDto>> RejectMemberAsync(
        int memberId, CancellationToken cancellationToken = default) =>
        PostAsync<object, CompanyMemberDto>(
            $"api/employer/company/members/{memberId}/reject", new { }, cancellationToken);
}
