using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.Repositories.Employer;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Employer;

/// <summary>
/// Candidate discovery for approved employers. Search is cross-company by design (a marketplace
/// of opted-in profiles), but every call is gated on Active membership of an Approved company.
/// </summary>
public sealed class CandidateSearchService(
    ApplicationDbContext dbContext,
    IApplicantRepository applicantRepository,
    TimeProvider timeProvider) : ICandidateSearchService
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 50;

    public async Task<ServiceResult<PagedResult<CandidateSearchItemDto>>> SearchAsync(
        string? userId,
        CandidateSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var employer = await EmployerGuard.RequireActiveAsync(applicantRepository, userId, cancellationToken);
        if (!employer.Success)
        {
            return ServiceResult<PagedResult<CandidateSearchItemDto>>.Fail(
                employer.ErrorType!.Value, employer.Errors);
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);
        var skillIds = await ResolveSkillIdsAsync(query, cancellationToken);
        var skillIdSet = skillIds.ToHashSet();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var candidates = dbContext.Candidates
            .AsNoTracking()
            .Where(candidate => candidate.IsDiscoverable);

        if (skillIdSet.Count > 0)
        {
            candidates = candidates.Where(candidate => candidate.Resumes
                .Any(resume => resume.ResumeSkills.Any(skill => skillIdSet.Contains(skill.SkillId))));
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = $"%{query.Keyword.Trim()}%";
            candidates = candidates.Where(candidate =>
                (candidate.Position != null && EF.Functions.Like(candidate.Position, pattern))
                || (candidate.Summary != null && EF.Functions.Like(candidate.Summary, pattern))
                || candidate.Resumes.Any(resume => resume.ResumeSkills
                    .Any(skill => EF.Functions.Like(skill.Skill.Name, pattern))));
        }

        var rows = await candidates
            .Select(candidate => new CandidateRow(
                candidate.Id,
                candidate.FullName,
                candidate.Position,
                candidate.Summary,
                candidate.AvatarUrl,
                candidate.PremiumUntil,
                candidate.BoostUntil))
            .ToListAsync(cancellationToken);

        // ponytail: per-skill max years and totals across resumes are aggregated in memory because
        // GroupBy over a join with Max is fragile across SQLite and Pomelo. The two queries stay narrow.
        var candidateIds = rows.Select(row => row.Id).ToList();
        var skillRows = candidateIds.Count == 0
            ? new List<SkillRow>()
            : await dbContext.ResumeSkills
                .AsNoTracking()
                .Where(skill => candidateIds.Contains(skill.Resume.CandidateId))
                .Select(skill => new SkillRow(
                    skill.Resume.CandidateId,
                    skill.SkillId,
                    skill.Skill.Name,
                    skill.YearsOfExperience))
                .ToListAsync(cancellationToken);

        var skillsByCandidate = skillRows
            .GroupBy(row => row.CandidateId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(row => row.SkillId)
                    .Select(skill => new CandidateSearchSkillDto(
                        skill.Key,
                        skill.First().Name,
                        skill.Max(row => row.YearsOfExperience)))
                    .OrderByDescending(skill => skill.YearsOfExperience)
                    .ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var matching = new List<CandidateSearchItemDto>();
        foreach (var row in rows)
        {
            var skills = skillsByCandidate.GetValueOrDefault(row.Id) ?? [];
            var matchingYears = skillIdSet.Count > 0
                ? skills.Where(skill => skillIdSet.Contains(skill.SkillId)).Sum(skill => skill.YearsOfExperience)
                : skills.Sum(skill => skill.YearsOfExperience);

            if (query.MinYears is { } minYears && minYears > 0 && matchingYears < minYears)
            {
                continue;
            }

            matching.Add(new CandidateSearchItemDto(
                row.Id,
                row.FullName,
                row.Position,
                row.Summary,
                row.AvatarUrl,
                skills,
                skills.Sum(skill => skill.YearsOfExperience),
                row.PremiumUntil is { } premiumUntil && premiumUntil > now,
                row.BoostUntil is { } boostUntil && boostUntil > now));
        }

        // Contract D12: boosted profiles first, then newest. Candidate has no CreatedAt yet, so the
        // monotonically increasing identity is the newest-first proxy.
        var ordered = matching
            .OrderByDescending(item => item.IsBoosted)
            .ThenByDescending(item => item.CandidateId)
            .ToList();

        return ServiceResult<PagedResult<CandidateSearchItemDto>>.Ok(new PagedResult<CandidateSearchItemDto>
        {
            Items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = ordered.Count,
            TotalPages = (int)Math.Ceiling(ordered.Count / (double)pageSize)
        });
    }

    public async Task<ServiceResult<IReadOnlyList<CandidateSearchSkillFilterDto>>> GetFilterSkillsAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var employer = await EmployerGuard.RequireActiveAsync(applicantRepository, userId, cancellationToken);
        if (!employer.Success)
        {
            return ServiceResult<IReadOnlyList<CandidateSearchSkillFilterDto>>.Fail(
                employer.ErrorType!.Value, employer.Errors);
        }

        var skills = await dbContext.Skills
            .AsNoTracking()
            .Where(skill => skill.ResumeSkills.Any(row => row.Resume.Candidate.IsDiscoverable))
            .OrderBy(skill => skill.Name)
            .Select(skill => new CandidateSearchSkillFilterDto(skill.Id, skill.Name, skill.Category))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<CandidateSearchSkillFilterDto>>.Ok(skills);
    }

    public async Task<ServiceResult<bool>> SetDiscoverableAsync(
        string? userId,
        bool isDiscoverable,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<bool>.Fail(ServiceErrorType.Unauthorized, "Not signed in.");
        }

        var candidate = await dbContext.Candidates
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (candidate is null)
        {
            return ServiceResult<bool>.Fail(ServiceErrorType.NotFound, "Candidate profile not found.");
        }

        candidate.IsDiscoverable = isDiscoverable;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(candidate.IsDiscoverable);
    }

    private async Task<List<int>> ResolveSkillIdsAsync(
        CandidateSearchQuery query,
        CancellationToken cancellationToken)
    {
        var skillIds = (query.SkillIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var names = (query.SkillNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return skillIds;
        }

        // ponytail: the skill table is a small reference table; loading it avoids relying on
        // provider-specific case-insensitive comparison for name filters.
        var allSkills = await dbContext.Skills
            .AsNoTracking()
            .Select(skill => new { skill.Id, skill.Name })
            .ToListAsync(cancellationToken);
        skillIds.AddRange(allSkills.Where(skill => names.Contains(skill.Name)).Select(skill => skill.Id));

        return skillIds.Distinct().ToList();
    }

    private sealed record CandidateRow(
        int Id,
        string FullName,
        string? Position,
        string? Summary,
        string? AvatarUrl,
        DateTime? PremiumUntil,
        DateTime? BoostUntil);

    private sealed record SkillRow(int CandidateId, int SkillId, string Name, int YearsOfExperience);
}
