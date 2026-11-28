using System.ComponentModel.DataAnnotations;
using hotjob.Data;
using hotjob.DTOs.Job;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.JobAlerts;
using hotjob.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Job;

public sealed class JobService(
    ApplicationDbContext dbContext,
    IJobPostingRepository jobRepository,
    IEntitlementService entitlementService,
    IJobAlertService jobAlertService,
    ILogger<JobService> logger,
    TimeProvider timeProvider,
    INotificationBroadcaster? broadcaster = null) : IJobService
{
    private readonly TimeProvider clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    private static readonly ApplicationStatus[] OpenStatuses =
    [
        ApplicationStatus.Pending,
        ApplicationStatus.Viewed,
        ApplicationStatus.Shortlisted,
        ApplicationStatus.Interviewing
    ];

    public async Task<ServiceResult<JobDetailResponse>> CreateAsync(
        string userId,
        CreateJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var employer = await GetAuthorizedEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        if (request is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Validation, "Job data is required.");
        }

        Normalize(request);
        var errors = await ValidateAsync(request, cancellationToken);
        if (errors.Count > 0)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Validation, errors);
        }

        var entitlement = await entitlementService.GetForCompanyAsync(
            employer.CompanyProfileId, employer.IsOwner, cancellationToken);
        if (entitlement is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(
                ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        if (!entitlement.CanAddJob)
        {
            return ServiceResult<JobDetailResponse>.Fail(
                ServiceErrorType.Conflict, JobSlotLimitMessage(entitlement));
        }

        var requestedStatus = request.Status ?? JobStatus.Draft;

        // Automatically convert Published to PendingApproval when creating
        var actualStatus = requestedStatus == JobStatus.Published ? JobStatus.PendingApproval : requestedStatus;

        var job = new JobPosting
        {
            CompanyProfileId = employer.CompanyProfileId,
            CreatedByEmployerId = employer.Id,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            SalaryMin = request.SalaryMin,
            SalaryMax = request.SalaryMax,
            ExperienceRequired = request.ExperienceRequired,
            JobType = request.JobType,
            ExpiryDate = request.ExpiryDate,
            Status = actualStatus,
            SubmittedForApprovalAt = actualStatus == JobStatus.PendingApproval ? DateTime.UtcNow : null,
            JobSkills = request.Skills
                .Select(skill => new JobSkill
                {
                    SkillId = skill.SkillId,
                    ImportanceWeight = skill.ImportanceWeight,
                    MinYears = skill.MinYears
                })
                .ToList(),
            CategoryMappings = request.CategoryIds
                .Select(categoryId => new JobCategoryMapping { JobCategoryId = categoryId })
                .ToList()
        };

        await jobRepository.AddAsync(job, cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Employer {EmployerId} created job {JobId} with status {Status}.", employer.Id, job.Id, actualStatus);

        var detail = await GetDetailAsync(employer.CompanyProfileId, job.Id, cancellationToken);
        if (job.Status == JobStatus.Published)
        {
            await TryNotifyPublishedAsync(job.Id, cancellationToken);
        }

        return detail;
    }

    public async Task<ServiceResult<JobDetailResponse>> UpdateAsync(
        string userId,
        int id,
        UpdateJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var employer = await GetAuthorizedEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        if (request is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Validation, "Job data is required.");
        }

        Normalize(request);
        var errors = await ValidateAsync(request, cancellationToken);
        if (errors.Count > 0)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Validation, errors);
        }

        var job = await jobRepository.GetByIdOwnedAsync(id, employer.CompanyProfileId, includeDetails: false, cancellationToken);
        if (job is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        var wasPublished = job.Status == JobStatus.Published;
        var requestedStatus = request.Status ?? job.Status;
        // Closed/Expired/Rejected jobs hold no quota slot; moving one back into the pipeline takes one.
        var reopening = job.Status is JobStatus.Closed or JobStatus.Expired or JobStatus.Rejected
            && requestedStatus is JobStatus.Draft or JobStatus.Published;
        if (reopening)
        {
            var entitlement = await entitlementService.GetForCompanyAsync(
                employer.CompanyProfileId, employer.IsOwner, cancellationToken);
            if (entitlement is null)
            {
                return ServiceResult<JobDetailResponse>.Fail(
                    ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
            }

            if (!entitlement.CanAddJob)
            {
                return ServiceResult<JobDetailResponse>.Fail(
                    ServiceErrorType.Conflict, JobSlotLimitMessage(entitlement));
            }
        }

        // Only an admin-approved (or already live) job can go Published. Asking to publish anything
        // else - a Draft, a Rejected job, or reopening a Closed/Expired one - submits it for (re)review,
        // mirroring CreateAsync.
        var submittingForApproval = requestedStatus == JobStatus.Published
            && job.Status is not (JobStatus.Approved or JobStatus.Published);
        if (submittingForApproval)
        {
            requestedStatus = JobStatus.PendingApproval;
            job.SubmittedForApprovalAt = DateTime.UtcNow;
            job.ReviewedAt = null;
            job.ReviewedByAdminId = null;
            job.RejectionReason = null;
        }

        job.Title = request.Title.Trim();
        job.Description = request.Description.Trim();
        job.SalaryMin = request.SalaryMin;
        job.SalaryMax = request.SalaryMax;
        var experienceChanged = job.ExperienceRequired != request.ExperienceRequired;
        job.ExperienceRequired = request.ExperienceRequired;
        job.JobType = request.JobType;
        job.ExpiryDate = request.ExpiryDate;
        job.Status = requestedStatus;

        var skillsChanged = ReplaceSkills(job, request.Skills);
        ReplaceCategories(job, request.CategoryIds);

        await jobRepository.SaveChangesAsync(cancellationToken);
        if (skillsChanged || experienceChanged)
        {
            await InvalidateMatchScoresAsync(job.Id, cancellationToken);
        }

        logger.LogInformation("Employer {EmployerId} updated job {JobId}.", employer.Id, job.Id);

        var detail = await GetDetailAsync(employer.CompanyProfileId, job.Id, cancellationToken);
        if (!wasPublished && job.Status == JobStatus.Published)
        {
            await TryNotifyPublishedAsync(job.Id, cancellationToken);
        }

        return detail;
    }

    public async Task<ServiceResult<JobDeleteResponse>> DeleteOrCloseAsync(
        string userId,
        int id,
        CancellationToken cancellationToken = default)
    {
        var employer = await GetAuthorizedEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<JobDeleteResponse>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        var job = await dbContext.JobPostings
            .Include(posting => posting.Applications).ThenInclude(application => application.Candidate)
            .FirstOrDefaultAsync(posting => posting.Id == id && posting.CompanyProfileId == employer.CompanyProfileId, cancellationToken);
        if (job is null)
        {
            return ServiceResult<JobDeleteResponse>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        if (job.Status == JobStatus.Draft)
        {
            dbContext.JobPostings.Remove(job);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Employer {EmployerId} deleted draft job {JobId}.", employer.Id, job.Id);
            return ServiceResult<JobDeleteResponse>.Ok(new JobDeleteResponse(id, Closed: false, JobStatus.Draft));
        }

        job.Status = JobStatus.Closed;
        var changedAt = clock.GetUtcNow().UtcDateTime;
        var notifiedUserIds = new List<string>();
        foreach (var application in job.Applications.Where(application => OpenStatuses.Contains(application.CurrentStatus)))
        {
            application.CurrentStatus = ApplicationStatus.Rejected;
            dbContext.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.Id,
                Status = ApplicationStatus.Rejected,
                ChangedByUserId = userId,
                Notes = "Job closed",
                ChangedAt = changedAt
            });
            dbContext.Notifications.Add(new Notification
            {
                UserId = application.Candidate.UserId,
                Title = "Application Rejected",
                Content = $"Your application for \"{job.Title}\" was rejected. Reason: Job closed.",
                IsRead = false,
                CreatedAt = changedAt
            });
            notifiedUserIds.Add(application.Candidate.UserId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (broadcaster is not null)
        {
            foreach (var userIdToNotify in notifiedUserIds)
                await broadcaster.NotifyAsync(userIdToNotify, "Application Rejected", cancellationToken);
        }

        logger.LogInformation("Employer {EmployerId} closed job {JobId}.", employer.Id, job.Id);
        return ServiceResult<JobDeleteResponse>.Ok(new JobDeleteResponse(id, Closed: true, JobStatus.Closed));
    }

    public async Task<ServiceResult<JobDetailResponse>> AssignCategoriesAsync(
        string userId,
        int id,
        AssignCategoriesRequest request,
        CancellationToken cancellationToken = default)
    {
        var employer = await GetAuthorizedEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        var categoryIds = (request?.CategoryIds ?? new List<int>()).Distinct().ToList();
        var errors = await MissingCategoriesAsync(categoryIds, cancellationToken);
        if (errors.Count > 0)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Validation, errors);
        }

        var job = await jobRepository.GetByIdOwnedAsync(id, employer.CompanyProfileId, includeDetails: false, cancellationToken);
        if (job is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        ReplaceCategories(job, categoryIds);
        await jobRepository.SaveChangesAsync(cancellationToken);

        return await GetDetailAsync(employer.CompanyProfileId, job.Id, cancellationToken);
    }

    public async Task<ServiceResult<JobDetailResponse>> AssignSkillsAsync(
        string userId,
        int id,
        AssignSkillsRequest request,
        CancellationToken cancellationToken = default)
    {
        var employer = await GetAuthorizedEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        var skills = DistinctSkills(request?.Skills ?? new List<JobSkillInput>());
        var errors = ValidateSkillRequirements(skills);
        errors.AddRange(await MissingSkillsAsync(skills.Select(skill => skill.SkillId).ToList(), cancellationToken));
        if (errors.Count > 0)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Validation, errors);
        }

        var job = await jobRepository.GetByIdOwnedAsync(id, employer.CompanyProfileId, includeDetails: false, cancellationToken);
        if (job is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        var skillsChanged = ReplaceSkills(job, skills);
        await jobRepository.SaveChangesAsync(cancellationToken);
        if (skillsChanged)
        {
            await InvalidateMatchScoresAsync(job.Id, cancellationToken);
        }

        return await GetDetailAsync(employer.CompanyProfileId, job.Id, cancellationToken);
    }

    public async Task<ServiceResult<IReadOnlyDictionary<JobStatus, int>>> GetStatusCountsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var employer = await GetAuthorizedEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<IReadOnlyDictionary<JobStatus, int>>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        // One GROUP BY instead of one COUNT round-trip per status.
        var rows = await dbContext.JobPostings
            .AsNoTracking()
            .Where(job => job.CompanyProfileId == employer.CompanyProfileId)
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<JobStatus, int> counts = Enum.GetValues<JobStatus>()
            .ToDictionary(status => status, status => rows.FirstOrDefault(row => row.Status == status)?.Count ?? 0);
        return ServiceResult<IReadOnlyDictionary<JobStatus, int>>.Ok(counts);
    }

    public async Task<ServiceResult<PagedResponse<JobListItemResponse>>> ListAsync(
        string userId,
        JobStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var employer = await GetAuthorizedEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<PagedResponse<JobListItemResponse>>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await jobRepository.ListOwnedAsync(employer.CompanyProfileId, status, search, page, pageSize, cancellationToken);

        return ServiceResult<PagedResponse<JobListItemResponse>>.Ok(new PagedResponse<JobListItemResponse>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<JobDetailResponse>> GetAsync(
        string userId,
        int id,
        CancellationToken cancellationToken = default)
    {
        var employer = await GetAuthorizedEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");
        }

        return await GetDetailAsync(employer.CompanyProfileId, id, cancellationToken);
    }

    public async Task<ServiceResult<PagedResponse<PublicJobListItemResponse>>> BrowsePublishedAsync(
        string? keyword,
        string? city,
        string? skill,
        int? companyId,
        JobType? jobType,
        int? categoryId,
        decimal? salaryMin,
        decimal? salaryMax,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (salaryMin.HasValue && salaryMax.HasValue && salaryMin.Value > salaryMax.Value)
        {
            return ServiceResult<PagedResponse<PublicJobListItemResponse>>.Fail(
                ServiceErrorType.Validation, "Salary minimum cannot be greater than salary maximum.");
        }

        if (salaryMin.HasValue && salaryMin.Value < 0 || salaryMax.HasValue && salaryMax.Value < 0)
        {
            return ServiceResult<PagedResponse<PublicJobListItemResponse>>.Fail(
                ServiceErrorType.Validation, "Salary cannot be negative.");
        }

        var (items, total) = await jobRepository.BrowsePublishedAsync(keyword, city, skill, companyId, jobType, categoryId, salaryMin, salaryMax, page, pageSize, cancellationToken);

        return ServiceResult<PagedResponse<PublicJobListItemResponse>>.Ok(new PagedResponse<PublicJobListItemResponse>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<IReadOnlyList<JobCategoryDto>>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await dbContext.JobCategories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new JobCategoryDto(category.Id, category.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<IReadOnlyList<JobCategoryDto>>.Ok(categories);
    }

    public async Task<ServiceResult<JobDetailResponse>> GetPublishedAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        var job = await jobRepository.GetPublishedByIdAsync(id, cancellationToken);
        if (job is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        var count = await dbContext.Applications.CountAsync(application => application.JobPostingId == id, cancellationToken);
        return ServiceResult<JobDetailResponse>.Ok(ToDetail(job, count));
    }

    public async Task<ServiceResult<PagedResponse<SavedJobDto>>> GetSavedJobsAsync(
        string? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var candidateId = await GetCandidateIdAsync(userId, cancellationToken);
        if (candidateId is null)
        {
            return ServiceResult<PagedResponse<SavedJobDto>>.Fail(
                ServiceErrorType.Unauthorized, "Candidate profile not found.");
        }

        IQueryable<int> visibleJobIds = dbContext.JobPostings
            .AsNoTracking()
            .WhereCompanyApproved()
            .Where(job => job.Company.IsActive)
            .Select(job => job.Id);

        var baseQuery = dbContext.SavedJobs
            .AsNoTracking()
            .Where(saved => saved.CandidateId == candidateId && visibleJobIds.Contains(saved.JobPostingId));

        var total = await baseQuery.CountAsync(cancellationToken);
        var items = await baseQuery
            .OrderByDescending(saved => saved.SavedAt)
            .ThenByDescending(saved => saved.JobPostingId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(saved => new SavedJobDto(
                saved.JobPostingId,
                saved.JobPosting.Title,
                saved.JobPosting.Company.CompanyName,
                saved.JobPosting.Company.Location,
                saved.JobPosting.SalaryMin,
                saved.JobPosting.SalaryMax,
                saved.JobPosting.JobType,
                saved.JobPosting.ExpiryDate,
                saved.JobPosting.Status,
                saved.SavedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<PagedResponse<SavedJobDto>>.Ok(new PagedResponse<SavedJobDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<bool>> IsSavedAsync(
        string? userId,
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var candidateId = await GetCandidateIdAsync(userId, cancellationToken);
        if (candidateId is null)
        {
            return ServiceResult<bool>.Ok(false);
        }

        var saved = await dbContext.SavedJobs
            .AsNoTracking()
            .AnyAsync(row => row.CandidateId == candidateId && row.JobPostingId == jobId, cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<bool>.Ok(saved);
    }

    public async Task<ServiceResult<bool>> SaveJobAsync(
        string? userId,
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var candidateId = await GetCandidateIdAsync(userId, cancellationToken);
        if (candidateId is null)
        {
            return ServiceResult<bool>.Fail(ServiceErrorType.Unauthorized, "Candidate profile not found.");
        }

        var now = DateTime.UtcNow;
        var isOpen = await dbContext.JobPostings
            .AsNoTracking()
            .Where(JobVisibility.OpenToCandidates(now))
            .AnyAsync(job => job.Id == jobId, cancellationToken)
            .ConfigureAwait(false);

        if (!isOpen)
        {
            return ServiceResult<bool>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        if (await dbContext.SavedJobs.AnyAsync(
                row => row.CandidateId == candidateId && row.JobPostingId == jobId, cancellationToken)
            .ConfigureAwait(false))
        {
            return ServiceResult<bool>.Ok(true);
        }

        dbContext.SavedJobs.Add(new SavedJob
        {
            CandidateId = candidateId.Value,
            JobPostingId = jobId,
            SavedAt = now
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent save from the same candidate wins; the bookmark still exists.
            foreach (var entry in dbContext.ChangeTracker.Entries<SavedJob>()
                         .Where(entry => entry.State == EntityState.Added)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            var exists = await dbContext.SavedJobs
                .AsNoTracking()
                .AnyAsync(row => row.CandidateId == candidateId && row.JobPostingId == jobId, cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
            {
                throw;
            }
        }

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> UnsaveJobAsync(
        string? userId,
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var candidateId = await GetCandidateIdAsync(userId, cancellationToken);
        if (candidateId is null)
        {
            return ServiceResult<bool>.Fail(ServiceErrorType.Unauthorized, "Candidate profile not found.");
        }

        await dbContext.SavedJobs
            .Where(row => row.CandidateId == candidateId && row.JobPostingId == jobId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<bool>.Ok(true);
    }

    private async Task<int?> GetCandidateIdAsync(string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var candidateId = await dbContext.Candidates
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidateId == 0 ? null : candidateId;
    }

    private async Task<ServiceResult<JobDetailResponse>> GetDetailAsync(
        int companyId,
        int jobId,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdOwnedAsync(jobId, companyId, includeDetails: true, cancellationToken);
        if (job is null)
        {
            return ServiceResult<JobDetailResponse>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        var count = await dbContext.Applications.CountAsync(application => application.JobPostingId == jobId, cancellationToken);
        return ServiceResult<JobDetailResponse>.Ok(ToDetail(job, count));
    }

    // Alerting is best-effort after the publish is saved: a failure must never roll back or
    // hide a job that is already Published, but it is always logged with the job id.
    private async Task TryNotifyPublishedAsync(int jobId, CancellationToken cancellationToken)
    {
        try
        {
            await jobAlertService.NotifyForPublishedJobAsync(jobId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Job alert generation failed for published job {JobId}; the job remains published.",
                jobId);
        }
    }

    private Task<global::hotjob.Models.Users.Employer?> GetAuthorizedEmployerAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Employers
            .Include(employer => employer.Company)
            .FirstOrDefaultAsync(
                employer => employer.UserId == userId
                    && employer.MembershipStatus == MembershipStatus.Active
                    && employer.IsActive
                    && employer.Company.Status == CompanyStatus.Approved
                    && employer.Company.IsActive,
                cancellationToken);

    private static void Normalize(CreateJobRequest request)
    {
        request.Title = request.Title?.Trim() ?? string.Empty;
        request.Description = request.Description?.Trim() ?? string.Empty;
        request.CategoryIds = request.CategoryIds?.Distinct().ToList() ?? new List<int>();
        request.Skills = DistinctSkills(request.Skills ?? new List<JobSkillInput>());
    }

    private static List<JobSkillInput> DistinctSkills(List<JobSkillInput> skills) =>
        skills.GroupBy(skill => skill.SkillId).Select(group => group.Last()).ToList();

    private async Task<List<string>> ValidateAsync(CreateJobRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true);
        errors.AddRange(validationResults.Select(result => result.ErrorMessage).OfType<string>());

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors.Add("Title is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            errors.Add("Description is required.");
        }

        if (request.SalaryMin.HasValue && request.SalaryMax.HasValue && request.SalaryMax < request.SalaryMin)
        {
            errors.Add("SalaryMax must be greater than or equal to SalaryMin.");
        }

        if (request.ExpiryDate.HasValue && request.ExpiryDate.Value <= DateTime.UtcNow)
        {
            errors.Add("ExpiryDate must be in the future.");
        }

        if (!Enum.IsDefined(request.JobType))
        {
            errors.Add("A valid job type is required.");
        }

        var status = request.Status ?? JobStatus.Draft;
        if (status is not JobStatus.Draft and not JobStatus.Published)
        {
            errors.Add("Only Draft or Published status is allowed. Close a job via DELETE.");
        }

        errors.AddRange(ValidateSkillRequirements(request.Skills));
        errors.AddRange(await MissingSkillsAsync(request.Skills.Select(skill => skill.SkillId).ToList(), cancellationToken));
        errors.AddRange(await MissingCategoriesAsync(request.CategoryIds, cancellationToken));

        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ValidateSkillRequirements(List<JobSkillInput> skills)
    {
        var errors = new List<string>();
        foreach (var skill in skills)
        {
            if (skill.ImportanceWeight is < 0 or > 10)
            {
                errors.Add($"ImportanceWeight for skill {skill.SkillId} must be between 0 and 10.");
            }

            if (skill.MinYears is < 0 or > 50)
            {
                errors.Add($"MinYears for skill {skill.SkillId} must be between 0 and 50.");
            }
        }

        return errors;
    }

    private async Task<List<string>> MissingSkillsAsync(List<int> skillIds, CancellationToken cancellationToken)
    {
        if (skillIds.Count == 0)
        {
            return new List<string>();
        }

        var found = await dbContext.Skills
            .Where(skill => skillIds.Contains(skill.Id))
            .Select(skill => skill.Id)
            .ToListAsync(cancellationToken);
        return skillIds.Except(found).Select(id => $"Skill {id} does not exist.").ToList();
    }

    private async Task<List<string>> MissingCategoriesAsync(List<int> categoryIds, CancellationToken cancellationToken)
    {
        if (categoryIds.Count == 0)
        {
            return new List<string>();
        }

        var found = await dbContext.JobCategories
            .Where(category => categoryIds.Contains(category.Id))
            .Select(category => category.Id)
            .ToListAsync(cancellationToken);
        return categoryIds.Except(found).Select(id => $"Category {id} does not exist.").ToList();
    }

    private static void ReplaceCategories(JobPosting job, List<int> categoryIds)
    {
        var wanted = categoryIds.ToHashSet();
        foreach (var mapping in job.CategoryMappings.Where(mapping => !wanted.Contains(mapping.JobCategoryId)).ToList())
        {
            job.CategoryMappings.Remove(mapping);
        }

        var existing = job.CategoryMappings.Select(mapping => mapping.JobCategoryId).ToHashSet();
        foreach (var categoryId in wanted.Where(id => !existing.Contains(id)))
        {
            job.CategoryMappings.Add(new JobCategoryMapping { JobPostingId = job.Id, JobCategoryId = categoryId });
        }
    }

    private static bool ReplaceSkills(JobPosting job, List<JobSkillInput> skills)
    {
        var wanted = skills.ToDictionary(
            skill => skill.SkillId,
            skill => (skill.ImportanceWeight, skill.MinYears));
        var changed = job.JobSkills.Count != wanted.Count
            || job.JobSkills.Any(jobSkill => !wanted.TryGetValue(jobSkill.SkillId, out var value)
                || value.ImportanceWeight != jobSkill.ImportanceWeight
                || value.MinYears != jobSkill.MinYears);

        foreach (var jobSkill in job.JobSkills.Where(jobSkill => !wanted.ContainsKey(jobSkill.SkillId)).ToList())
        {
            job.JobSkills.Remove(jobSkill);
        }

        foreach (var jobSkill in job.JobSkills)
        {
            var value = wanted[jobSkill.SkillId];
            jobSkill.ImportanceWeight = value.ImportanceWeight;
            jobSkill.MinYears = value.MinYears;
        }

        var existing = job.JobSkills.Select(jobSkill => jobSkill.SkillId).ToHashSet();
        foreach (var (skillId, value) in wanted.Where(pair => !existing.Contains(pair.Key)))
        {
            job.JobSkills.Add(new JobSkill
            {
                JobPostingId = job.Id,
                SkillId = skillId,
                ImportanceWeight = value.ImportanceWeight,
                MinYears = value.MinYears
            });
        }

        return changed;
    }

    private async Task InvalidateMatchScoresAsync(int jobId, CancellationToken cancellationToken)
    {
        // ponytail: delete instead of stamping — the next job view recomputes and upserts.
        await dbContext.MatchScores
            .Where(score => score.JobPostingId == jobId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string JobSlotLimitMessage(EmployerEntitlement entitlement) =>
        $"You have used all {entitlement.ActiveJobCount}/{entitlement.JobQuota} job slots on your plan. Upgrade to post more.";

    private static JobDetailResponse ToDetail(JobPosting job, int applicationsCount) =>
        new(
            job.Id,
            job.CompanyProfileId,
            job.Title,
            job.Description,
            job.SalaryMin,
            job.SalaryMax,
            job.ExperienceRequired,
            job.JobType,
            job.ExpiryDate,
            job.Status,
            job.Company?.CompanyName ?? string.Empty,
            job.JobSkills.Select(jobSkill => new JobSkillDto(
                jobSkill.SkillId,
                jobSkill.Skill?.Name ?? string.Empty,
                jobSkill.ImportanceWeight,
                jobSkill.MinYears)).ToList(),
            job.CategoryMappings.Select(mapping => new JobCategoryDto(mapping.JobCategoryId, mapping.Category?.Name ?? string.Empty)).ToList(),
            applicationsCount,
            job.CreatedAt,
            job.Company?.Location,
            job.FeaturedUntil,
            job.SubmittedForApprovalAt,
            job.ReviewedAt,
            job.RejectionReason);

    public async Task<ServiceResult<PagedResponse<SkillMatchJobListItemDto>>> GetJobsMatchingResumeAsync(
        string userId,
        int resumeId,
        string? city,
        JobType? jobType,
        int minSkillMatches,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        minSkillMatches = Math.Clamp(minSkillMatches, 1, 50);

        var resume = await dbContext.Resumes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == resumeId, cancellationToken)
            .ConfigureAwait(false);

        if (resume is null || resume.CandidateId == 0)
        {
            return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Fail(
                ServiceErrorType.NotFound,
                "Resume not found.");
        }

        var ownerUserId = await dbContext.Candidates
            .Where(candidate => candidate.Id == resume.CandidateId)
            .Select(candidate => candidate.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ownerUserId != userId)
        {
            return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Fail(
                ServiceErrorType.NotFound,
                "Resume not found.");
        }

        var now = DateTime.UtcNow;
        var resumeSkillIds = await dbContext.ResumeSkills
            .AsNoTracking()
            .Where(resumeSkill => resumeSkill.ResumeId == resumeId)
            .Select(resumeSkill => resumeSkill.SkillId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (resumeSkillIds.Count == 0)
        {
            return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Ok(
                new PagedResponse<SkillMatchJobListItemDto>
                {
                    Items = Array.Empty<SkillMatchJobListItemDto>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize
                });
        }

        var query = dbContext.JobPostings
            .AsNoTracking()
            .Where(JobVisibility.OpenToCandidates(now))
            .Where(job => job.JobSkills.Any(jobSkill => resumeSkillIds.Contains(jobSkill.SkillId)));

        if (!string.IsNullOrWhiteSpace(city))
        {
            var term = city.Trim();
            query = query.Where(job => job.Company.Location != null
                && EF.Functions.Like(job.Company.Location, $"%{term}%"));
        }

        if (jobType.HasValue)
        {
            query = query.Where(job => job.JobType == jobType.Value);
        }

        var projected = query
            .Select(job => new
            {
                job.Id,
                job.Title,
                CompanyName = job.Company.CompanyName,
                Location = job.Company.Location,
                job.SalaryMin,
                job.SalaryMax,
                job.ExperienceRequired,
                job.JobType,
                job.ExpiryDate,
                MatchedSkillCount = job.JobSkills.Count(
                    jobSkill => resumeSkillIds.Contains(jobSkill.SkillId)),
                MatchPercentage = job.MatchScores
                    .Where(score => score.ResumeId == resumeId)
                    .Select(score => (double?)score.MatchPercentage)
                    .FirstOrDefault(),
                job.CreatedAt,
                job.FeaturedUntil
            })
            .Where(row => row.MatchedSkillCount >= minSkillMatches);

        var total = await projected.CountAsync(cancellationToken).ConfigureAwait(false);
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
        {
            return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Ok(
                new PagedResponse<SkillMatchJobListItemDto>
                {
                    TotalCount = total,
                    Page = page,
                    PageSize = pageSize
                });
        }

        var rows = await projected
            .OrderByDescending(row => row.MatchedSkillCount)
            .ThenByDescending(row => row.MatchPercentage ?? 0)
            .ThenByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var jobIds = rows.Select(row => row.Id).ToList();

        var matchedSkillRows = await dbContext.JobSkills
            .AsNoTracking()
            .Where(jobSkill => jobIds.Contains(jobSkill.JobPostingId)
                && resumeSkillIds.Contains(jobSkill.SkillId))
            .Select(jobSkill => new
            {
                jobSkill.JobPostingId,
                jobSkill.SkillId,
                SkillName = jobSkill.Skill.Name
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var skillsByJob = matchedSkillRows
            .GroupBy(row => row.JobPostingId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MatchedSkillDto>)group
                    .OrderBy(row => row.SkillName)
                    .Select(row => new MatchedSkillDto(row.SkillId, row.SkillName))
                    .ToList());

        var items = rows
            .Select(row => new SkillMatchJobListItemDto(
                row.Id,
                row.Title,
                row.CompanyName,
                row.Location,
                row.SalaryMin,
                row.SalaryMax,
                row.ExperienceRequired,
                row.JobType,
                row.ExpiryDate,
                row.MatchedSkillCount,
                skillsByJob.GetValueOrDefault(row.Id) ?? Array.Empty<MatchedSkillDto>(),
                row.MatchPercentage,
                row.CreatedAt,
                row.FeaturedUntil))
            .ToList();

        return ServiceResult<PagedResponse<SkillMatchJobListItemDto>>.Ok(
            new PagedResponse<SkillMatchJobListItemDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            });
    }
}
