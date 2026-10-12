using hotjob.DTOs.Employer;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Repositories.Employer;
using hotjob.Services.Common;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Employer;

public sealed class InterviewService(
    IApplicantRepository repo,
    ILogger<InterviewService> logger) : IInterviewService
{
    public async Task<ServiceResult<InterviewResponse>> CreateAsync(
        string? userId,
        int applicationId,
        CreateInterviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var employerResult = await EmployerGuard.RequireActiveAsync(repo, userId, cancellationToken);
        if (!employerResult.Success)
            return ServiceResult<InterviewResponse>.Fail(employerResult.ErrorType!.Value, employerResult.Errors);

        if (request.InterviewDate.ToUniversalTime() <= DateTime.UtcNow)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "InterviewDate must be in the future.");
        if (!request.InterviewType.HasValue || !Enum.IsDefined(request.InterviewType.Value))
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "A valid interview type is required.");

        Interview interview;
        await using var tx = await repo.BeginTransactionAsync(cancellationToken);
        try
        {
            var app = await repo.GetOwnedForUpdateAsync(
                employerResult.Data!.CompanyProfileId, applicationId, cancellationToken);
            if (app is null)
                return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.NotFound, "Application not found.");
            if (app.CurrentStatus is ApplicationStatus.Offered or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn)
                return ServiceResult<InterviewResponse>.Fail(
                    ServiceErrorType.Conflict, $"Cannot schedule an interview for an application with status {app.CurrentStatus}.");

            interview = new Interview
            {
                ApplicationId = app.Id,
                InterviewDate = request.InterviewDate.ToUniversalTime(),
                LocationOrLink = string.IsNullOrWhiteSpace(request.LocationOrLink) ? null : request.LocationOrLink.Trim(),
                InterviewType = request.InterviewType,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                Status = InterviewStatus.Scheduled
            };
            repo.AddInterview(interview);

            // ponytail: auto-advance only Shortlisted→Interviewing; other states schedule without status change.
            if (app.CurrentStatus == ApplicationStatus.Shortlisted)
            {
                app.CurrentStatus = ApplicationStatus.Interviewing;
                repo.AddHistory(new ApplicationStatusHistory
                {
                    ApplicationId = app.Id,
                    Status = ApplicationStatus.Interviewing,
                    ChangedByUserId = userId,
                    Notes = "Interview scheduled.",
                    ChangedAt = DateTime.UtcNow
                });
            }

            repo.AddNotification(new Notification
            {
                UserId = app.Candidate.UserId,
                Title = "Interview scheduled",
                Content = $"An interview for \"{app.JobPosting.Title}\" is scheduled at {interview.InterviewDate:u}."
                    + (string.IsNullOrWhiteSpace(interview.LocationOrLink) ? string.Empty : $" Location: {interview.LocationOrLink}."),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await repo.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Create interview for application {ApplicationId} failed.", applicationId);
            throw;
        }

        return ServiceResult<InterviewResponse>.Ok(ApplicantMapping.ToInterviewResponse(interview));
    }

    public async Task<ServiceResult<IReadOnlyList<InterviewResponse>>> ListAsync(
        string? userId,
        int applicationId,
        CancellationToken cancellationToken = default)
    {
        var employerResult = await EmployerGuard.RequireActiveAsync(repo, userId, cancellationToken);
        if (!employerResult.Success)
            return ServiceResult<IReadOnlyList<InterviewResponse>>.Fail(employerResult.ErrorType!.Value, employerResult.Errors);

        if (!await repo.IsApplicationOwnedAsync(employerResult.Data!.CompanyProfileId, applicationId, cancellationToken))
            return ServiceResult<IReadOnlyList<InterviewResponse>>.Fail(ServiceErrorType.NotFound, "Application not found.");

        var interviews = await repo.ListInterviewsAsync(applicationId, cancellationToken);
        return ServiceResult<IReadOnlyList<InterviewResponse>>.Ok(
            interviews.Select(ApplicantMapping.ToInterviewResponse).ToArray());
    }

    public async Task<ServiceResult<InterviewResponse>> UpdateAsync(
        string? userId,
        int interviewId,
        UpdateInterviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var employerResult = await EmployerGuard.RequireActiveAsync(repo, userId, cancellationToken);
        if (!employerResult.Success)
            return ServiceResult<InterviewResponse>.Fail(employerResult.ErrorType!.Value, employerResult.Errors);

        if (request.InterviewType.HasValue && !Enum.IsDefined(request.InterviewType.Value))
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "Invalid interview type.");
        if (request.Status.HasValue && !Enum.IsDefined(request.Status.Value))
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "Invalid interview status.");
        if (request.InterviewDate.HasValue && request.InterviewDate.Value.ToUniversalTime() <= DateTime.UtcNow)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "InterviewDate must be in the future.");

        var interview = await repo.GetOwnedInterviewForUpdateAsync(
            employerResult.Data!.CompanyProfileId, interviewId, cancellationToken);
        if (interview is null)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.NotFound, "Interview not found.");

        if (request.InterviewDate.HasValue)
            interview.InterviewDate = request.InterviewDate.Value.ToUniversalTime();
        if (request.LocationOrLink is not null)
            interview.LocationOrLink = string.IsNullOrWhiteSpace(request.LocationOrLink) ? null : request.LocationOrLink.Trim();
        if (request.InterviewType.HasValue)
            interview.InterviewType = request.InterviewType;
        if (request.Notes is not null)
            interview.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (request.Status.HasValue)
            interview.Status = request.Status.Value;

        await repo.SaveChangesAsync(cancellationToken);
        return ServiceResult<InterviewResponse>.Ok(ApplicantMapping.ToInterviewResponse(interview));
    }

    public async Task<ServiceResult<InterviewResponse>> CancelAsync(
        string? userId,
        int interviewId,
        CancellationToken cancellationToken = default)
    {
        var employerResult = await EmployerGuard.RequireActiveAsync(repo, userId, cancellationToken);
        if (!employerResult.Success)
            return ServiceResult<InterviewResponse>.Fail(employerResult.ErrorType!.Value, employerResult.Errors);

        var interview = await repo.GetOwnedInterviewForUpdateAsync(
            employerResult.Data!.CompanyProfileId, interviewId, cancellationToken);
        if (interview is null)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.NotFound, "Interview not found.");

        interview.Status = InterviewStatus.Cancelled;
        await repo.SaveChangesAsync(cancellationToken);
        return ServiceResult<InterviewResponse>.Ok(ApplicantMapping.ToInterviewResponse(interview));
    }
}
