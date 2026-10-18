using hotjob.Models;
using hotjob.Models.Users;

namespace hotjob.Repositories.Company;

public interface ICompanyRepository
{
    Task<(IReadOnlyList<CompanyProfile> Items, int TotalCount)> ListApprovedAsync(
        string? search,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> locations,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<string> Industries, IReadOnlyList<string> Locations)> ListFilterOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<CompanyProfile?> GetApprovedByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<string?> GetOwnerNameAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Review> Items, int TotalCount)> ListReviewsAsync(
        int companyId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
