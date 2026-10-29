using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Candidate;

/// <summary>
/// Apply and withdraw rules for a signed-in candidate. Ownership is checked by
/// candidate profile id on every write, so ids from another account are a
/// not-found rather than a cross-tenant edit.
/// </summary>
public sealed class ApplicationService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<ApplicationService> logger) : IApplicationService
{
    public async Task<ServiceResult<ApplyContextDto>> GetApplyContextAsync(
        string? userId,
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<ApplyContextDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var resumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.CandidateId == candidate.Data.Id)
            .OrderByDescending(resume => resume.IsPrimary)
            .ThenByDescending(resume => resume.CreatedAt)
            .ThenByDescending(resume => resume.Id)
            .Select(resume => new ResumeOptionDto(resume.Id, resume.Title, resume.IsPrimary, resume.FileUrl))
            .ToListAsync(cancellationToken);

        var application = await dbContext.Applications
            .AsNoTracking()
            .Where(row => row.JobPostingId == jobId && row.CandidateId == candidate.Data.Id)
            .Select(row => new { row.Id, row.CurrentStatus, row.AppliedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return ServiceResult<ApplyContextDto>.Ok(new ApplyContextDto
        {
            AlreadyApplied = application is not null,
            ApplicationId = application?.Id,
            CurrentStatus = application?.CurrentStatus,
            AppliedAt = application?.AppliedAt,
            Resumes = resumes
        });
    }

    public async Task<ServiceResult<ApplyResultDto>> ApplyAsync(
        string? userId,
        int jobId,
        int resumeId,
        string? coverLetter,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult<ApplyResultDto>.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var normalizedCoverLetter = Normalize(coverLetter);
        if (normalizedCoverLetter?.Length > 2000)
        {
            return ServiceResult<ApplyResultDto>.Fail(
                ServiceErrorType.Validation, "Cover letter must be at most 2000 characters.");
        }

        var job = await dbContext.JobPostings
            .AsNoTracking()
            .Where(row => row.Id == jobId)
            .Select(row => new
            {
                row.Id,
                row.Status,
                row.ExpiryDate,
                row.CompanyProfileId,
                row.Title
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
            return ServiceResult<ApplyResultDto>.Fail(ServiceErrorType.NotFound, "Job not found.");
        if (job.Status != JobStatus.Published)
        {
            return ServiceResult<ApplyResultDto>.Fail(
                ServiceErrorType.Conflict, "This job is not accepting applications.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (job.ExpiryDate is { } expiry && expiry <= now)
        {
            return ServiceResult<ApplyResultDto>.Fail(
                ServiceErrorType.Conflict, "This job is no longer accepting applications.");
        }

        var resume = await dbContext.Resumes
            .AsNoTracking()
            .Where(row => row.Id == resumeId && row.CandidateId == candidate.Data.Id)
            .Select(row => new { row.Id, row.Title, row.Summary, row.FileUrl })
            .FirstOrDefaultAsync(cancellationToken);
        if (resume is null)
            return ServiceResult<ApplyResultDto>.Fail(ServiceErrorType.NotFound, "Resume not found.");

        var alreadyApplied = await dbContext.Applications
            .AsNoTracking()
            .AnyAsync(
                row => row.JobPostingId == jobId && row.CandidateId == candidate.Data.Id,
                cancellationToken);
        if (alreadyApplied)
            return DuplicateApplication();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        Application? application = null;
        ApplicationStatusHistory? history = null;
        try
        {
            application = new Application
            {
                JobPostingId = job.Id,
                ResumeId = resume.Id,
                CandidateId = candidate.Data.Id,
                CoverLetter = normalizedCoverLetter,
                CurrentStatus = ApplicationStatus.Pending,
                SnapshotSummary = resume.Summary,
                SnapshotFileUrl = resume.FileUrl,
                AppliedAt = now
            };
            dbContext.Applications.Add(application);
            await dbContext.SaveChangesAsync(cancellationToken);

            history = new ApplicationStatusHistory
            {
                ApplicationId = application.Id,
                Status = ApplicationStatus.Pending,
                ChangedByUserId = candidate.Data.UserId,
                Notes = "Application submitted",
                ChangedAt = now
            };
            dbContext.ApplicationStatusHistories.Add(history);

            var employerUserIds = await dbContext.Employers
                .AsNoTracking()
                .Where(employer => employer.CompanyProfileId == job.CompanyProfileId
                    && employer.MembershipStatus == MembershipStatus.Active)
                .Select(employer => employer.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var employerUserId in employerUserIds)
            {
                dbContext.Notifications.Add(new Notification
                {
                    UserId = employerUserId,
                    Title = $"New application for {job.Title}",
                    Content = $"{candidate.Data.FullName} applied for \"{job.Title}\" with the resume \"{resume.Title}\".",
                    IsRead = false,
                    CreatedAt = now
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (application is not null)
                dbContext.Entry(application).State = EntityState.Detached;
            if (history is not null)
                dbContext.Entry(history).State = EntityState.Detached;

            // ponytail: the unique (job, candidate) index is the hard guarantee; a racing
            // second request lands here and must read as a friendly conflict, not a 500.
            var duplicate = await dbContext.Applications
                .AsNoTracking()
                .AnyAsync(
                    row => row.JobPostingId == jobId && row.CandidateId == candidate.Data.Id,
                    cancellationToken);
            if (duplicate)
                return DuplicateApplication();

            logger.LogError(exception, "Apply to job {JobId} for candidate {CandidateId} failed.",
                jobId, candidate.Data.Id);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Apply to job {JobId} for candidate {CandidateId} failed.",
                jobId, candidate.Data.Id);
            throw;
        }

        logger.LogInformation("Candidate {CandidateId} applied to job {JobId} with resume {ResumeId}.",
            candidate.Data.Id, jobId, resumeId);
        return ServiceResult<ApplyResultDto>.Ok(
            new ApplyResultDto(application!.Id, application.CurrentStatus, application.AppliedAt));
    }

    public async Task<ServiceResult> WithdrawAsync(
        string? userId,
        int applicationId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await ResolveCandidateAsync(userId, cancellationToken);
        if (!candidate.Success)
            return ServiceResult.Fail(candidate.ErrorType!.Value, candidate.Errors);

        var application = await dbContext.Applications.FirstOrDefaultAsync(
            row => row.Id == applicationId && row.CandidateId == candidate.Data.Id,
            cancellationToken);
        if (application is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Application not found.");

        if (application.CurrentStatus is ApplicationStatus.Offered
            or ApplicationStatus.Rejected
            or ApplicationStatus.Withdrawn)
        {
            return ServiceResult.Fail(
                ServiceErrorType.Conflict,
                $"An application that is {application.CurrentStatus} can no longer be withdrawn.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        application.CurrentStatus = ApplicationStatus.Withdrawn;
        dbContext.ApplicationStatusHistories.Add(new ApplicationStatusHistory
        {
            ApplicationId = application.Id,
            Status = ApplicationStatus.Withdrawn,
            ChangedByUserId = candidate.Data.UserId,
            Notes = "Withdrawn by candidate",
            ChangedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Candidate {CandidateId} withdrew application {ApplicationId}.",
            candidate.Data.Id, applicationId);
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult<(int Id, string UserId, string FullName)>> ResolveCandidateAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<(int, string, string)>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");

        var candidate = await dbContext.Candidates
            .AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new { row.Id, row.UserId, row.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        return candidate is null
            ? ServiceResult<(int, string, string)>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.")
            : ServiceResult<(int, string, string)>.Ok((candidate.Id, candidate.UserId, candidate.FullName));
    }

    private static ServiceResult<ApplyResultDto> DuplicateApplication() =>
        ServiceResult<ApplyResultDto>.Fail(
            ServiceErrorType.Conflict, "You have already applied to this job.");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
