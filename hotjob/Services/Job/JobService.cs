using System.ComponentModel.DataAnnotations;
using hotjob.Data;
using hotjob.DTOs.Job;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Job;

public sealed class JobService(
    ApplicationDbContext dbContext,
    IJobPostingRepository jobRepository,
    ILogger<JobService> logger) : IJobService
{
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
            Status = request.Status ?? JobStatus.Draft,
            JobSkills = request.Skills
                .Select(skill => new JobSkill { SkillId = skill.SkillId, ImportanceWeight = skill.ImportanceWeight })
                .ToList(),
            CategoryMappings = request.CategoryIds
                .Select(categoryId => new JobCategoryMapping { JobCategoryId = categoryId })
                .ToList()
        };

        await jobRepository.AddAsync(job, cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Employer {EmployerId} created job {JobId}.", employer.Id, job.Id);

        return await GetDetailAsync(employer.CompanyProfileId, job.Id, cancellationToken);
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

        job.Title = request.Title.Trim();
        job.Description = request.Description.Trim();
        job.SalaryMin = request.SalaryMin;
        job.SalaryMax = request.SalaryMax;
        job.ExperienceRequired = request.ExperienceRequired;
        job.JobType = request.JobType;
        job.ExpiryDate = request.ExpiryDate;
        job.Status = request.Status ?? job.Status;

        ReplaceSkills(job, request.Skills);
        ReplaceCategories(job, request.CategoryIds);

        await jobRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Employer {EmployerId} updated job {JobId}.", employer.Id, job.Id);

        return await GetDetailAsync(employer.CompanyProfileId, job.Id, cancellationToken);
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
            .Include(posting => posting.Applications)
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
        foreach (var application in job.Applications.Where(application => OpenStatuses.Contains(application.CurrentStatus)))
        {
            application.CurrentStatus = ApplicationStatus.Rejected;
            dbContext.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.Id,
                Status = ApplicationStatus.Rejected,
                ChangedByUserId = userId,
                Notes = "Job closed",
                ChangedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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
        var errors = ValidateSkillWeights(skills);
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

        ReplaceSkills(job, skills);
        await jobRepository.SaveChangesAsync(cancellationToken);

        return await GetDetailAsync(employer.CompanyProfileId, job.Id, cancellationToken);
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
        var counts = await ApplicationCountsAsync(items.Select(job => job.Id).ToList(), cancellationToken);

        return ServiceResult<PagedResponse<JobListItemResponse>>.Ok(new PagedResponse<JobListItemResponse>
        {
            Items = items.Select(job => ToListItem(job, counts.GetValueOrDefault(job.Id))).ToList(),
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
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await jobRepository.BrowsePublishedAsync(keyword, city, skill, companyId, jobType, page, pageSize, cancellationToken);

        return ServiceResult<PagedResponse<PublicJobListItemResponse>>.Ok(new PagedResponse<PublicJobListItemResponse>
        {
            Items = items.Select(ToPublicListItem).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
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

    private Task<global::hotjob.Models.Users.Employer?> GetAuthorizedEmployerAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Employers
            .Include(employer => employer.Company)
            .FirstOrDefaultAsync(
                employer => employer.UserId == userId
                    && employer.MembershipStatus == MembershipStatus.Active
                    && employer.Company.Status == CompanyStatus.Approved,
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

        errors.AddRange(ValidateSkillWeights(request.Skills));
        errors.AddRange(await MissingSkillsAsync(request.Skills.Select(skill => skill.SkillId).ToList(), cancellationToken));
        errors.AddRange(await MissingCategoriesAsync(request.CategoryIds, cancellationToken));

        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ValidateSkillWeights(List<JobSkillInput> skills)
    {
        var errors = new List<string>();
        foreach (var skill in skills)
        {
            if (skill.ImportanceWeight is < 0 or > 10)
            {
                errors.Add($"ImportanceWeight for skill {skill.SkillId} must be between 0 and 10.");
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

    private static void ReplaceSkills(JobPosting job, List<JobSkillInput> skills)
    {
        var wanted = skills.ToDictionary(skill => skill.SkillId, skill => skill.ImportanceWeight);
        foreach (var jobSkill in job.JobSkills.Where(jobSkill => !wanted.ContainsKey(jobSkill.SkillId)).ToList())
        {
            job.JobSkills.Remove(jobSkill);
        }

        foreach (var jobSkill in job.JobSkills)
        {
            jobSkill.ImportanceWeight = wanted[jobSkill.SkillId];
        }

        var existing = job.JobSkills.Select(jobSkill => jobSkill.SkillId).ToHashSet();
        foreach (var (skillId, weight) in wanted.Where(pair => !existing.Contains(pair.Key)))
        {
            job.JobSkills.Add(new JobSkill { JobPostingId = job.Id, SkillId = skillId, ImportanceWeight = weight });
        }
    }

    private async Task<Dictionary<int, int>> ApplicationCountsAsync(List<int> jobIds, CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        return await dbContext.Applications
            .Where(application => jobIds.Contains(application.JobPostingId))
            .GroupBy(application => application.JobPostingId)
            .Select(group => new { JobId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.JobId, row => row.Count, cancellationToken);
    }

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
            job.JobSkills.Select(jobSkill => new JobSkillDto(jobSkill.SkillId, jobSkill.Skill?.Name ?? string.Empty, jobSkill.ImportanceWeight)).ToList(),
            job.CategoryMappings.Select(mapping => new JobCategoryDto(mapping.JobCategoryId, mapping.Category?.Name ?? string.Empty)).ToList(),
            applicationsCount,
            job.CreatedAt,
            job.Company?.Location);

    private static PublicJobListItemResponse ToPublicListItem(JobPosting job) =>
        new(
            job.Id,
            job.Title,
            job.Company?.CompanyName ?? string.Empty,
            job.Company?.Location,
            job.SalaryMin,
            job.SalaryMax,
            job.JobType,
            job.ExpiryDate,
            job.CreatedAt,
            job.JobSkills.Select(jobSkill => jobSkill.Skill?.Name ?? string.Empty).Where(name => name.Length > 0).ToList());

    private static JobListItemResponse ToListItem(JobPosting job, int applicationsCount) =>
        new(
            job.Id,
            job.Title,
            job.SalaryMin,
            job.SalaryMax,
            job.ExperienceRequired,
            job.JobType,
            job.ExpiryDate,
            job.Status,
            job.Company?.CompanyName ?? string.Empty,
            job.JobSkills.Count,
            job.CategoryMappings.Select(mapping => mapping.Category?.Name ?? string.Empty).ToList(),
            applicationsCount,
            job.CreatedAt);
}
