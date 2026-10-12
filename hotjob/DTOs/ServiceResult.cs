namespace hotjob.DTOs;

/// <summary>
/// Generic service result wrapper for consistent API responses
/// </summary>
/// <typeparam name="T">Type of data returned on success</typeparam>
public class ServiceResult<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    /// <summary>
    /// Creates a successful result with data
    /// </summary>
    public static ServiceResult<T> Ok(T data, string message = "Success")
    {
        return new ServiceResult<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    /// <summary>
    /// Creates a successful result without data
    /// </summary>
    public static ServiceResult<T> Ok(string message = "Success")
    {
        return new ServiceResult<T>
        {
            Success = true,
            Message = message
        };
    }

    /// <summary>
    /// Creates a failed result with error message
    /// </summary>
    public static ServiceResult<T> Fail(string message, Dictionary<string, string[]>? errors = null)
    {
        return new ServiceResult<T>
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }

    /// <summary>
    /// Creates a failed result with validation errors
    /// </summary>
    public static ServiceResult<T> ValidationFail(Dictionary<string, string[]> errors)
    {
        return new ServiceResult<T>
        {
            Success = false,
            Message = "Validation failed",
            Errors = errors
        };
    }
}

/// <summary>
/// Non-generic service result for operations that don't return data
/// </summary>
public class ServiceResult : ServiceResult<object>
{
    public new static ServiceResult Ok(string message = "Success")
    {
        return new ServiceResult
        {
            Success = true,
            Message = message
        };
    }

    public new static ServiceResult Fail(string message, Dictionary<string, string[]>? errors = null)
    {
        return new ServiceResult
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }
}
