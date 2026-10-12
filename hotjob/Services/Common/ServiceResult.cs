using System;
using System.Collections.Generic;
using System.Linq;

namespace hotjob.Services.Common;

public enum ServiceErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized
}

public class ServiceResult
{
    public bool Success { get; protected init; }
    public IReadOnlyList<string> Errors { get; protected init; } = Array.Empty<string>();
    public ServiceErrorType? ErrorType { get; protected init; }

    public static ServiceResult Ok()
    {
        return new ServiceResult { Success = true };
    }

    public static ServiceResult Fail(ServiceErrorType errorType, params string[] errors)
    {
        return Fail(errorType, (IEnumerable<string>)errors);
    }

    public static ServiceResult Fail(ServiceErrorType errorType, IEnumerable<string> errors)
    {
        return new ServiceResult
        {
            Success = false,
            ErrorType = errorType,
            Errors = errors
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .ToArray()
        };
    }
}

public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; private init; }

    public static ServiceResult<T> Ok(T data)
    {
        return new ServiceResult<T>
        {
            Success = true,
            Data = data                                   
        };
    }

    public new static ServiceResult<T> Fail(ServiceErrorType errorType, params string[] errors)
    {
        return Fail(errorType, (IEnumerable<string>)errors);
    }

    public new static ServiceResult<T> Fail(ServiceErrorType errorType, IEnumerable<string> errors)
    {
        return new ServiceResult<T>
        {
            Success = false,
            ErrorType = errorType,
            Errors = errors
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .ToArray()
        };
    }
}
