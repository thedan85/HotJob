using System.Net;
using hotjob.DTOs.Authentication;

namespace hotjob.Services.Api;

/// <summary>Typed client for the account controller (<c>api/account</c>), which returns raw bodies.</summary>
public sealed class AccountApiClient(
    IHttpClientFactory httpClientFactory,
    hotjob.Services.Authentication.AuthSession session,
    hotjob.Services.Authentication.HotJobAuthStateProvider authStateProvider,
    Microsoft.AspNetCore.Components.NavigationManager navigationManager,
    Microsoft.JSInterop.IJSRuntime js)
    : ApiClientBase(httpClientFactory, session, authStateProvider, navigationManager, js)
{
    public Task<ApiResult<ProfileResponse>> GetProfileAsync(CancellationToken cancellationToken = default) =>
        GetRawAsync<ProfileResponse>("api/account/profile", cancellationToken);

    public Task<ApiResult<ProfileResponse>> UpdateProfileAsync(
        UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
        PutRawAsync<UpdateProfileRequest, ProfileResponse>("api/account/profile", request, cancellationToken);

    public Task<ApiResult<RegisteredAccountResponse>> RegisterAsync(
        RegisterAccountRequest request, CancellationToken cancellationToken = default) =>
        // AllowAnonymous: no Bearer, raw RegisteredAccountResponse body.
        PostRawAsync<RegisterAccountRequest, RegisteredAccountResponse>(
            "api/account/register", request, cancellationToken, anonymous: true);

    public Task<ApiResult<object>> ChangePasswordAsync(
        ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        // 204 NoContent has no body; any 2xx is success. ponytail: no UI calls this yet.
        PostRawAsync<ChangePasswordRequest, object>("api/account/change-password", request, cancellationToken);
}
