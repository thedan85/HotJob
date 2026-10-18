using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Repositories.Company;

public sealed class CompanyRepository(ApplicationDbContext dbContext) : ICompanyRepository
{
    public async Task<(IReadOnlyList<CompanyProfile> Items, int TotalCount)> ListApprovedAsync(
        string? search,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> locations,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLower();
        var industrySet = NormalizeSet(industries);
        var locationSet = NormalizeSet(locations);

        var query = dbContext.CompanyProfiles
            .AsNoTracking()
            .Where(company => company.Status == CompanyStatus.Approved);

        if (term is not null)
            query = query.Where(company => company.CompanyName.ToLower().Contains(term));

        if (industrySet.Count > 0)
            query = query.Where(company => company.Industry != null && industrySet.Contains(company.Industry.ToLower()));

        if (locationSet.Count > 0)
            query = query.Where(company => company.Location != null && locationSet.Contains(company.Location.ToLower()));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(company => company.CompanyName)
            .ThenBy(company => company.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(IReadOnlyList<string> Industries, IReadOnlyList<string> Locations)> ListFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var industries = await dbContext.CompanyProfiles
            .AsNoTracking()
            .Where(company => company.Status == CompanyStatus.Approved && company.Industry != null)
            .Select(company => company.Industry!)
            .ToListAsync(cancellationToken);

        var locations = await dbContext.CompanyProfiles
            .AsNoTracking()
            .Where(company => company.Status == CompanyStatus.Approved && company.Location != null)
            .Select(company => company.Location!)
            .ToListAsync(cancellationToken);

        return (Dedupe(industries), Dedupe(locations));
    }

    public Task<CompanyProfile?> GetApprovedByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        dbContext.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                company => company.Id == id && company.Status == CompanyStatus.Approved,
                cancellationToken);

    public Task<string?> GetOwnerNameAsync(
        int companyId,
        CancellationToken cancellationToken = default) =>
        dbContext.Employers
            .AsNoTracking()
            .Where(employer => employer.CompanyProfileId == companyId
                && employer.IsOwner
                && employer.MembershipStatus == MembershipStatus.Active)
            .Select(employer => employer.FullName)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<Review> Items, int TotalCount)> ListReviewsAsync(
        int companyId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Reviews
            .AsNoTracking()
            .Include(review => review.Candidate)
            .Where(review => review.CompanyProfileId == companyId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    private static HashSet<string> NormalizeSet(IReadOnlyList<string>? values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null) return set;
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                set.Add(value.Trim().ToLower());
        }
        return set;
    }

    private static IReadOnlyList<string> Dedupe(List<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var value in values)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed)) continue;
            ordered.Add(trimmed);
        }
        ordered.Sort(StringComparer.OrdinalIgnoreCase);
        return ordered;
    }
}
