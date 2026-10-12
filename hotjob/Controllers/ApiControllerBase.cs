using HotJob.Dtos;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>
/// Shared envelope helpers for controllers. Keeps success/error shapes
/// (<see cref="ApiResponse{T}"/>) identical across the API surface.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Maps a <see cref="ServiceResult{T}"/> to <c>200</c> on success or a status-coded envelope on failure.</summary>
    protected IActionResult FromResult<T>(ServiceResult<T> result) =>
        result.Success ? Ok(ToEnvelope(result.Data)) : FromError(result);

    /// <summary>Same as <see cref="FromResult{T}"/> but returns <c>201 Created</c> on success.</summary>
    protected IActionResult FromResultCreated<T>(ServiceResult<T> result) =>
        result.Success
            ? StatusCode(StatusCodes.Status201Created, ToEnvelope(result.Data))
            : FromError(result);

    /// <summary>Error-only mapping, for actions that return the unit <see cref="ServiceResult"/>.</summary>
    protected IActionResult FromError(ServiceResult result) =>
        result.ErrorType switch
        {
            ServiceErrorType.Conflict => Conflict(ToErrorPayload(result)),
            ServiceErrorType.NotFound => NotFound(ToErrorPayload(result)),
            ServiceErrorType.Unauthorized => Unauthorized(ToErrorPayload(result)),
            _ => BadRequest(ToErrorPayload(result))
        };

    private static ApiResponse<T> ToEnvelope<T>(T? data) => new() { Success = true, Data = data };

    private static ApiResponse<object> ToErrorPayload(ServiceResult result) =>
        new() { Success = false, Errors = result.Errors.ToList() };
}
