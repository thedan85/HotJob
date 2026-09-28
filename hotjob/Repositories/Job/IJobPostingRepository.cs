using hotjob.Models.Job;

namespace hotjob.Repositories.Job;

public interface IJobPostingRepository
{
    Task<JobPosting?> GetByIdOwnedAsync(
        int id,
        int companyId,
        bool includeDetails,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<JobPosting> Items, int TotalCount)> ListOwnedAsync(
        int companyId,
        JobStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task AddAsync(JobPosting job, CancellationToken cancellationToken = default);

    void Remove(JobPosting job);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
