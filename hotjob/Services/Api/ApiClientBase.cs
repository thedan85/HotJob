using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HotJob.Dtos;
using hotjob.Components;
using hotjob.Services.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace hotjob.Services.Api;

/// <summary>Base for typed server-side HTTP clients calling backend controllers.</summary>
/// <remarks>Attaches the circuit Bearer token per request; on 401 does one
/// <c>hotjobAuth.me</c> JS refresh then a single retry. Transport errors become
/// friendly <see cref="ApiResult{T}"/> failures, never exceptions to Razor.</remarks>
public abstract class ApiClientBase(
    IHttpClientFactory httpClientFactory,
    AuthSession session,
    HotJobAuthStateProvider authStateProvider,
    NavigationManager navigationManager,
    IJSRuntime js)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default, bool anonymous = false) =>
        SendAsync<T>(HttpMethod.Get, path, null, cancellationToken, anonymous);

    protected Task<ApiResult<TResponse>> PostAsync<TRequest, TResponse>(
        string path, TRequest body, CancellationToken cancellationToken = default, bool anonymous = false) =>
        SendAsync<TResponse>(HttpMethod.Post, path, body, cancellationToken, anonymous);

    protected Task<ApiResult<TResponse>> PutAsync<TRequest, TResponse>(
        string path, TRequest body, CancellationToken cancellationToken = default, bool anonymous = false) =>
        SendAsync<TResponse>(HttpMethod.Put, path, body, cancellationToken, anonymous);

    protected Task<ApiResult<TResponse>> PatchAsync<TRequest, TResponse>(
        string path, TRequest body, CancellationToken cancellationToken = default, bool anonymous = false) =>
        SendAsync<TResponse>(new HttpMethod("PATCH"), path, body, cancellationToken, anonymous);

    protected Task<ApiResult<T>> DeleteAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Delete, path, null, cancellationToken, anonymous: false);

    // AccountController returns raw bodies (ProfileResponse / string[] errors), not the
    // ApiResponse<T> envelope the employer controllers use. Raw helpers parse that shape.
    protected Task<ApiResult<T>> GetRawAsync<T>(string path, CancellationToken cancellationToken = default, bool anonymous = false) =>
        SendRawAsync<T>(HttpMethod.Get, path, null, cancellationToken, anonymous);

    protected Task<ApiResult<TResponse>> PostRawAsync<TRequest, TResponse>(
        string path, TRequest? body, CancellationToken cancellationToken = default, bool anonymous = false) =>
        SendRawAsync<TResponse>(HttpMethod.Post, path, body, cancellationToken, anonymous);

    protected Task<ApiResult<TResponse>> PutRawAsync<TRequest, TResponse>(
        string path, TRequest body, CancellationToken cancellationToken = default, bool anonymous = false) =>
        SendRawAsync<TResponse>(HttpMethod.Put, path, body, cancellationToken, anonymous);

    protected static string Query(string path, params (string Name, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value!.Trim())}")
            .ToArray();
        return parts.Length == 0 ? path : $"{path}?{string.Join("&", parts)}";
    }

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool anonymous)
    {
        var noRetry = await TrySendAsync<T>(method, path, body, cancellationToken, anonymous);
        if (anonymous || noRetry.Status != HttpStatusCode.Unauthorized)
        {
            return noRetry;
        }

        if (!await TryRefreshSessionAsync())
        {
            return noRetry;
        }

        return await TrySendAsync<T>(method, path, body, cancellationToken, anonymous);
    }

    private async Task<ApiResult<T>> TrySendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool anonymous)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(nameof(ApiClientBase));
            using var request = new HttpRequestMessage(method, new Uri(navigationManager.BaseUri).Combine(path));
            if (!anonymous && session.Tokens?.AccessToken is { Length: > 0 } token)
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            return await ReadAsync<T>(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Fail(
                "We could not reach the server. Please try again.",
                HttpStatusCode.ServiceUnavailable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ApiResult<T>.Fail(exception.Message, HttpStatusCode.ServiceUnavailable);
        }
    }

    private async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ApiResponse<T>? envelope = null;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        var errors = envelope?.Errors is { Count: > 0 }
            ? envelope.Errors.ToArray()
            : response.IsSuccessStatusCode
                ? Array.Empty<string>()
                : new[] { $"Request failed ({(int)response.StatusCode})." };
        var data = envelope is null ? default(T) : envelope.Data;
        return new ApiResult<T>(
            response.IsSuccessStatusCode && (envelope?.Success ?? false),
            data,
            errors,
            response.StatusCode);
    }

    private async Task<ApiResult<T>> SendRawAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool anonymous)
    {
        var noRetry = await TrySendRawAsync<T>(method, path, body, cancellationToken, anonymous);
        if (anonymous || noRetry.Status != HttpStatusCode.Unauthorized)
        {
            return noRetry;
        }

        if (!await TryRefreshSessionAsync())
        {
            return noRetry;
        }

        return await TrySendRawAsync<T>(method, path, body, cancellationToken, anonymous);
    }

    private async Task<ApiResult<T>> TrySendRawAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool anonymous)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(nameof(ApiClientBase));
            using var request = new HttpRequestMessage(method, new Uri(navigationManager.BaseUri).Combine(path));
            if (!anonymous && session.Tokens?.AccessToken is { Length: > 0 } token)
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            return await ReadRawAsync<T>(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Fail(
                "We could not reach the server. Please try again.",
                HttpStatusCode.ServiceUnavailable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ApiResult<T>.Fail(exception.Message, HttpStatusCode.ServiceUnavailable);
        }
    }

    private static async Task<ApiResult<T>> ReadRawAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return ApiResult<T>.Fail(await ReadRawErrorsAsync(response, cancellationToken), response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return ApiResult<T>.Ok(default, response.StatusCode);
        }

        T? data = default;
        try
        {
            data = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            data = default;
        }

        return ApiResult<T>.Ok(data, response.StatusCode);
    }

    private static async Task<IReadOnlyList<string>> ReadRawErrorsAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var list = await response.Content.ReadFromJsonAsync<List<string>>(Json, cancellationToken);
            if (list is { Count: > 0 })
            {
                return list.ToArray();
            }
        }
        catch (JsonException)
        {
        }

        // AccountController returns Unauthorized("Not authenticated.") as plain text.
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return [text.Trim()];
            }
        }
        catch (Exception)
        {
        }

        return [$"Request failed ({(int)response.StatusCode})."];
    }

    private async Task<bool> TryRefreshSessionAsync()
    {
        try
        {
            var result = await js.InvokeAsync<AuthApiResult>("hotjobAuth.me");
            if (!result.Ok || result.Data is null)
            {
                return false;
            }

            authStateProvider.SignIn(result.ToTokens());
            return true;
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            // JS unavailable (prerender) or refresh failed: caller surfaces Unauthorized.
            return false;
        }
    }
}

internal static class UriCombineExtensions
{
    public static Uri Combine(this Uri baseUri, string path) =>
        new(baseUri, path.TrimStart('/'));
}
