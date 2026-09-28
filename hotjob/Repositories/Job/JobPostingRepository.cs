using hotjob.Data;
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

    public async Task<(IReadOnlyList<JobPosting> Items, int TotalCount)> ListOwnedAsync(
        int companyId,
        JobStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = DetailsQuery().Where(job => job.CompanyProfileId == companyId);

        if (status.HasValue)
        {
            query = query.Where(job => job.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(job =>
                EF.Functions.Like(job.Title, $"%{term}%") ||
                EF.Functions.Like(job.Description, $"%{term}%"));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
