using System.Net;

namespace hotjob.Services.Api;

/// <summary>Result of a typed API call from a Blazor circuit to a backend controller.</summary>
/// <typeparam name="T">Payload type on success.</typeparam>
/// <param name="Success">True when the HTTP call succeeded and the body parsed.</param>
/// <param name="Data">Payload on success, otherwise default.</param>
/// <param name="Errors">Human-readable errors for Razor display.</param>
/// <param name="Status">HTTP status of the final attempt.</param>
public sealed record ApiResult<T>(
    bool Success,
    T? Data,
    IReadOnlyList<string> Errors,
    HttpStatusCode Status)
{
    public static ApiResult<T> Ok(T? data, HttpStatusCode status = HttpStatusCode.OK) =>
        new(true, data, [], status);

    public static ApiResult<T> Fail(IEnumerable<string> errors, HttpStatusCode status) =>
        new(false, default, errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray(), status);

    public static ApiResult<T> Fail(string error, HttpStatusCode status) =>
        Fail([error], status);
}
