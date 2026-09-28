using hotjob.DTOs.Employer;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Repositories.Employer;
using hotjob.Services.Common;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Employer;

public sealed class ApplicantService(
    IApplicantRepository repo,
    ILogger<ApplicantService> logger) : IApplicantService
{
    public async Task<ServiceResult<PagedResult<ApplicantListItemResponse>>> ListAsync(
        string? userId,
        ApplicantListQuery query,
        CancellationToken cancellationToken = default)
    {
        var employerResult = await EmployerGuard.RequireActiveAsync(repo, userId, cancellationToken);
        if (!employerResult.Success)
            return ServiceResult<PagedResult<ApplicantListItemResponse>>.Fail(employerResult.ErrorType!.Value, employerResult.Errors);

        query.Page = Math.Max(query.Page, 1);
        query.PageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await repo.ListOwnedAsync(
            employerResult.Data!.CompanyProfileId,
            query.JobPostingId, query.Status,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.Page, query.PageSize, cancellationToken);

        return ServiceResult<PagedResult<ApplicantListItemResponse>>.Ok(new PagedResult<ApplicantListItemResponse>
        {
            Items = items.Select(ApplicantMapping.ToListItem).ToArray(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        });
    }

    public async Task<ServiceResult<ApplicantDetailResponse>> GetDetailAsync(
        string? userId,
        int applicationId,
        CancellationToken cancellationToken = default)
    {
        var employerResult = await EmployerGuard.RequireActiveAsync(repo, userId, cancellationToken);
        if (!employerResult.Success)
            return ServiceResult<ApplicantDetailResponse>.Fail(employerResult.ErrorType!.Value, employerResult.Errors);

        var app = await repo.GetOwnedDetailAsync(employerResult.Data!.CompanyProfileId, applicationId, cancellationToken);
        if (app is null)
            return ServiceResult<ApplicantDetailResponse>.Fail(ServiceErrorType.NotFound, "Application not found.");

        return ServiceResult<ApplicantDetailResponse>.Ok(ApplicantMapping.ToDetail(app));
    }

    public async Task<ServiceResult<ApplicantDetailResponse>> UpdateStatusAsync(
        string? userId,
        int applicationId,
        UpdateStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var notes = request.Notes?.Trim();
        var target = request.Status;

        if (!Enum.IsDefined(target))
            return ServiceResult<ApplicantDetailResponse>.Fail(ServiceErrorType.Validation, "Invalid status value.");
        if (target == ApplicationStatus.Withdrawn)
            return ServiceResult<ApplicantDetailResponse>.Fail(
                ServiceErrorType.Validation, "Employers cannot mark an application as Withdrawn.");

        var employerResult = await EmployerGuard.RequireActiveAsync(repo, userId, cancellationToken);
        if (!employerResult.Success)
            return ServiceResult<ApplicantDetailResponse>.Fail(employerResult.ErrorType!.Value, employerResult.Errors);

        await using var tx = await repo.BeginTransactionAsync(cancellationToken);
        try
        {
            var app = await repo.GetOwnedForUpdateAsync(
                employerResult.Data!.CompanyProfileId, applicationId, cancellationToken);
            if (app is null)
                return ServiceResult<ApplicantDetailResponse>.Fail(ServiceErrorType.NotFound, "Application not found.");

            if (!IsTransitionAllowed(app.CurrentStatus, target))
                return ServiceResult<ApplicantDetailResponse>.Fail(
                    ServiceErrorType.Conflict,
                    $"Cannot change status from {app.CurrentStatus} to {target}.");

            if (app.CurrentStatus != target)
            {
                app.CurrentStatus = target;
                repo.AddHistory(new ApplicationStatusHistory
                {
                    ApplicationId = app.Id,
                    Status = target,
                    ChangedByUserId = userId,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                    ChangedAt = DateTime.UtcNow
                });
                repo.AddNotification(new Notification
                {
                    UserId = app.Candidate.UserId,
                    Title = $"Application {target}",
                    Content = string.IsNullOrWhiteSpace(notes)
                        ? $"Your application for \"{app.JobPosting.Title}\" is now {target}."
                        : $"Your application for \"{app.JobPosting.Title}\" is now {target}. {notes}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await repo.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Update application {ApplicationId} status failed.", applicationId);
            throw;
        }

        return await GetDetailAsync(userId, applicationId, cancellationToken);
    }

    /// <summary>Forward-only: Pending→Viewed→Shortlisted→Interviewing→Offered; Rejected reachable from Pending/Viewed/Shortlisted/Interviewing. Offered/Rejected/Withdrawn are terminal.</summary>
    internal static bool IsTransitionAllowed(ApplicationStatus current, ApplicationStatus target)
    {
        if (current == target)
            return true;
        if (current is ApplicationStatus.Offered or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn)
            return false;
        if (target == ApplicationStatus.Rejected)
            return current is ApplicationStatus.Pending or ApplicationStatus.Viewed
                or ApplicationStatus.Shortlisted or ApplicationStatus.Interviewing;
        return (current, target) switch
        {
            (ApplicationStatus.Pending, ApplicationStatus.Viewed) => true,
            (ApplicationStatus.Viewed, ApplicationStatus.Shortlisted) => true,
            (ApplicationStatus.Shortlisted, ApplicationStatus.Interviewing) => true,
            (ApplicationStatus.Interviewing, ApplicationStatus.Offered) => true,
            _ => false
        };
    }
}
