using hotjob.DTOs.Job;
using hotjob.Models.Job;

namespace hotjob.Repositories.Job;

public interface IJobPostingRepository
{
    Task<JobPosting?> GetByIdOwnedAsync(
        int id,
        int companyId,
        bool includeDetails,
        CancellationToken cancellationToken = default);

    Task<JobPosting?> GetPublishedByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>One page of the company's jobs, projected to list rows (no entity tracking).</summary>
    Task<(IReadOnlyList<JobListItemResponse> Items, int TotalCount)> ListOwnedAsync(
        int companyId,
        JobStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>One page of jobs open to candidates, projected to public list rows.</summary>
    Task<(IReadOnlyList<PublicJobListItemResponse> Items, int TotalCount)> BrowsePublishedAsync(
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
        CancellationToken cancellationToken = default);

    Task AddAsync(JobPosting job, CancellationToken cancellationToken = default);

    void Remove(JobPosting job);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
