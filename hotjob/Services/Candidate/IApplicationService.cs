using hotjob.DTOs.Candidate;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

/// <summary>Candidate actions on job applications: pre-apply context, apply, withdraw.</summary>
public interface IApplicationService
{
    /// <summary>Loads the caller's resumes and whether they already applied to the job.</summary>
    Task<ServiceResult<ApplyContextDto>> GetApplyContextAsync(
        string? userId,
        int jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Submits an application with a frozen resume snapshot.</summary>
    Task<ServiceResult<ApplyResultDto>> ApplyAsync(
        string? userId,
        int jobId,
        int resumeId,
        string? coverLetter,
        CancellationToken cancellationToken = default);

    /// <summary>Moves the caller's in-progress application to Withdrawn.</summary>
    Task<ServiceResult> WithdrawAsync(
        string? userId,
        int applicationId,
        CancellationToken cancellationToken = default);
}
