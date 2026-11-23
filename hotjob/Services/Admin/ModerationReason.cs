using hotjob.Services.Common;

namespace hotjob.Services.Admin;

/// <summary>Shared normalization/validation for free-text moderation reasons (reject, deactivate, delete).</summary>
internal static class ModerationReason
{
    public const int MaxLength = 500;

    /// <summary>Trims the reason; blank input becomes <c>null</c>.</summary>
    public static string? Normalize(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    /// <summary>Returns a validation failure when the normalized reason is missing or too long; otherwise <c>null</c>.</summary>
    public static ServiceResult? ValidateRequired(string? normalized) =>
        normalized is null
            ? ServiceResult.Fail(ServiceErrorType.Validation, "A reason is required.")
            : ValidateOptional(normalized);

    /// <summary>Returns a validation failure when the normalized reason is too long; otherwise <c>null</c>.</summary>
    public static ServiceResult? ValidateOptional(string? normalized) =>
        normalized is { Length: > MaxLength }
            ? ServiceResult.Fail(ServiceErrorType.Validation, $"Reason must be at most {MaxLength} characters.")
            : null;
}
