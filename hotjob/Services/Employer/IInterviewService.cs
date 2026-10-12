using hotjob.DTOs.Employer;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

public interface IInterviewService
{
    Task<ServiceResult<InterviewResponse>> CreateAsync(
        string? userId,
        int applicationId,
        CreateInterviewRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<InterviewResponse>>> ListAsync(
        string? userId,
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InterviewResponse>> UpdateAsync(
        string? userId,
        int interviewId,
        UpdateInterviewRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InterviewResponse>> CancelAsync(
        string? userId,
        int interviewId,
        CancellationToken cancellationToken = default);
}
