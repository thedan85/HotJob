using hotjob.DTOs.Employer;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Repositories.Employer;
using hotjob.Services.Common;
using hotjob.Services.Notifications;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Employer;

public sealed class InterviewService(
    IApplicantRepository repo,
    TimeProvider timeProvider,
    ILogger<InterviewService> logger,
    INotificationBroadcaster? broadcaster = null) : IInterviewService
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (request.InterviewDate.ToUniversalTime() <= now)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "InterviewDate must be in the future.");
        if (!request.InterviewType.HasValue || !Enum.IsDefined(request.InterviewType.Value))
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "A valid interview type is required.");
        if (request.Round is < 1 or > 100)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "Round must be between 1 and 100.");

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

            var candidateUserId = app.Candidate.UserId;
            interview = new Interview
            {
                ApplicationId = app.Id,
                Round = request.Round,
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
                    ChangedAt = now
                });
            }

            repo.AddNotification(new Notification
            {
                UserId = candidateUserId,
                Title = "Interview scheduled",
                Content = Cap($"Round {interview.Round}: an interview for \"{app.JobPosting.Title}\" is scheduled at {interview.InterviewDate:u}."
                    + (string.IsNullOrWhiteSpace(interview.LocationOrLink) ? string.Empty : $" Location: {interview.LocationOrLink}.")),
                IsRead = false,
                CreatedAt = now
            });

            await repo.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            if (broadcaster is not null)
                await broadcaster.NotifyAsync(candidateUserId, "Interview scheduled", cancellationToken);
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (request.InterviewType.HasValue && !Enum.IsDefined(request.InterviewType.Value))
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "Invalid interview type.");
        if (request.Status.HasValue && !Enum.IsDefined(request.Status.Value))
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "Invalid interview status.");
        if (request.InterviewDate.HasValue && request.InterviewDate.Value.ToUniversalTime() <= now)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "InterviewDate must be in the future.");
        if (request.Round is < 1 or > 100)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.Validation, "Round must be between 1 and 100.");

        var hasChanges = request.Round.HasValue || request.InterviewDate.HasValue || request.LocationOrLink is not null
            || request.InterviewType.HasValue || request.Notes is not null || request.Status.HasValue;

        var interview = await repo.GetOwnedInterviewForUpdateAsync(
            employerResult.Data!.CompanyProfileId, interviewId, cancellationToken);
        if (interview is null)
            return ServiceResult<InterviewResponse>.Fail(ServiceErrorType.NotFound, "Interview not found.");

        if (request.Round.HasValue)
            interview.Round = request.Round.Value;
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

        if (hasChanges)
        {
            repo.AddNotification(new Notification
            {
                UserId = interview.Application.Candidate.UserId,
                Title = "Interview updated",
                Content = Cap($"Round {interview.Round}: the interview for \"{interview.Application.JobPosting.Title}\" was updated to {interview.InterviewDate:u}."
                    + (string.IsNullOrWhiteSpace(interview.LocationOrLink) ? string.Empty : $" Location: {interview.LocationOrLink}.")),
                IsRead = false,
                CreatedAt = now
            });
        }

        await repo.SaveChangesAsync(cancellationToken);
        if (hasChanges && broadcaster is not null)
            await broadcaster.NotifyAsync(interview.Application.Candidate.UserId, "Interview updated", cancellationToken);
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
        repo.AddNotification(new Notification
        {
            UserId = interview.Application.Candidate.UserId,
            Title = "Interview cancelled",
            Content = $"Round {interview.Round}: the interview for \"{interview.Application.JobPosting.Title}\" scheduled at {interview.InterviewDate:u} was cancelled.",
            IsRead = false,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        });

        await repo.SaveChangesAsync(cancellationToken);
        if (broadcaster is not null)
            await broadcaster.NotifyAsync(interview.Application.Candidate.UserId, "Interview cancelled", cancellationToken);
        return ServiceResult<InterviewResponse>.Ok(ApplicantMapping.ToInterviewResponse(interview));
    }

    private static string Cap(string content) => content.Length <= 2000 ? content : content[..2000];
}
