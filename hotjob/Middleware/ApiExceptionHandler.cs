using HotJob.Dtos;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace hotjob.Middleware;

/// <summary>
/// JSON fallback for unhandled exceptions on <c>/api</c>: returns 500 with the
/// <see cref="ApiResponse{T}"/> envelope instead of the HTML error page.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
            return false;

        logger.LogError(exception, "Unhandled API exception for {Path}.", httpContext.Request.Path);
        var payload = new ApiResponse<object>
        {
            Success = false,
            Errors = ["An unexpected error occurred."]
        };
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(payload, cancellationToken);
        return true;
    }
}
