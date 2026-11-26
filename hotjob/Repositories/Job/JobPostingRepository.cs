using hotjob.Data;
using hotjob.DTOs.Job;
using hotjob.Models.Job;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Repositories.Job;

public sealed class JobPostingRepository(ApplicationDbContext dbContext) : IJobPostingRepository
{
    public Task<JobPosting?> GetByIdOwnedAsync(
        int id,
        int companyId,
        bool includeDetails,
        CancellationToken cancellationToken = default)
    {
        var query = includeDetails
            ? DetailsQuery().Where(job => job.Id == id && job.CompanyProfileId == companyId)
            : dbContext.JobPostings
                .Include(job => job.JobSkills)
                .Include(job => job.CategoryMappings)
                .Where(job => job.Id == id && job.CompanyProfileId == companyId);

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    public Task<JobPosting?> GetPublishedByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return DetailsQuery()
            .Where(JobVisibility.OpenToCandidates(now))
            .Where(job => job.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<JobListItemResponse> Items, int TotalCount)> ListOwnedAsync(
        int companyId,
        JobStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.JobPostings
            .AsNoTracking()
            .Where(job => job.CompanyProfileId == companyId);

        if (status.HasValue)
        {
            query = query.Where(job => job.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // The employer UI promises "Title, skill or category".
            query = query.Where(job =>
                EF.Functions.Like(job.Title, $"%{term}%") ||
                EF.Functions.Like(job.Description, $"%{term}%") ||
                job.JobSkills.Any(jobSkill => EF.Functions.Like(jobSkill.Skill.Name, $"%{term}%")) ||
                job.CategoryMappings.Any(mapping => EF.Functions.Like(mapping.Category.Name, $"%{term}%")));
        }

        var total = await query.CountAsync(cancellationToken);
        // Projection: only list columns (no Description), counts as subqueries, and a single
        // collection (category names) so the page loads in one round trip without a cartesian join.
        var items = await query
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(job => new JobListItemResponse(
                job.Id,
                job.Title,
                job.SalaryMin,
                job.SalaryMax,
                job.ExperienceRequired,
                job.JobType,
                job.ExpiryDate,
                job.Status,
                job.Company.CompanyName,
                job.JobSkills.Count,
                job.CategoryMappings.Select(mapping => mapping.Category.Name).ToList(),
                job.Applications.Count,
                job.CreatedAt,
                job.FeaturedUntil,
                job.Status == JobStatus.Rejected ? job.RejectionReason : null,
                job.ModerationReason))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(IReadOnlyList<PublicJobListItemResponse> Items, int TotalCount)> BrowsePublishedAsync(
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

        var now = DateTime.UtcNow;
        var query = dbContext.JobPostings
            .AsNoTracking()
            .Where(JobVisibility.OpenToCandidates(now));

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(job =>
                EF.Functions.Like(job.Title, $"%{term}%") ||
                EF.Functions.Like(job.Description, $"%{term}%") ||
                EF.Functions.Like(job.Company.CompanyName, $"%{term}%") ||
                job.JobSkills.Any(jobSkill => EF.Functions.Like(jobSkill.Skill.Name, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            var term = city.Trim();
            query = query.Where(job =>
                job.Company.Location != null && EF.Functions.Like(job.Company.Location, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(skill))
        {
            var term = skill.Trim();
            query = query.Where(job =>
                job.JobSkills.Any(jobSkill => EF.Functions.Like(jobSkill.Skill.Name, $"%{term}%")));
        }

        if (companyId is > 0)
        {
            query = query.Where(job => job.CompanyProfileId == companyId.Value);
        }

        if (jobType.HasValue)
        {
            query = query.Where(job => job.JobType == jobType.Value);
        }

        if (categoryId is > 0)
        {
            query = query.Where(job =>
                job.CategoryMappings.Any(mapping => mapping.JobCategoryId == categoryId.Value));
        }

        // Open-ended jobs (missing bound) still match any range the user enters.
        if (salaryMin.HasValue)
        {
            query = query.Where(job => job.SalaryMax == null || job.SalaryMax >= salaryMin.Value);
        }

        if (salaryMax.HasValue)
        {
            query = query.Where(job => job.SalaryMin == null || job.SalaryMin <= salaryMax.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(job => job.FeaturedUntil != null && job.FeaturedUntil > now)
            .ThenByDescending(job => job.FeaturedUntil)
            .ThenByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // Only the card's columns plus skill names; categories and Description are not loaded.
            .Select(job => new PublicJobListItemResponse(
                job.Id,
                job.Title,
                job.Company.CompanyName,
                job.Company.Location,
                job.SalaryMin,
                job.SalaryMax,
                job.JobType,
                job.ExpiryDate,
                job.CreatedAt,
                job.JobSkills
                    .Where(jobSkill => jobSkill.Skill.Name != "")
                    .Select(jobSkill => jobSkill.Skill.Name)
                    .ToList(),
                job.FeaturedUntil))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task AddAsync(JobPosting job, CancellationToken cancellationToken = default)
    {
        return dbContext.JobPostings.AddAsync(job, cancellationToken).AsTask();
    }

    public void Remove(JobPosting job)
    {
        dbContext.JobPostings.Remove(job);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<JobPosting> DetailsQuery() =>
        dbContext.JobPostings
            .AsNoTracking()
            .Include(job => job.Company)
            .Include(job => job.JobSkills).ThenInclude(jobSkill => jobSkill.Skill)
            .Include(job => job.CategoryMappings).ThenInclude(mapping => mapping.Category);
}
