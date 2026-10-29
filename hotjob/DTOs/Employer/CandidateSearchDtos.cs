namespace hotjob.DTOs.Employer;

/// <summary>Filters and paging accepted by employer candidate search.</summary>
public sealed class CandidateSearchQuery
{
    /// <summary>Selected skill ids. A candidate matches when any of them appears on a resume.</summary>
    public IReadOnlyList<int> SkillIds { get; set; } = Array.Empty<int>();

    /// <summary>Selected skill names, resolved to ids before searching. Matches any selected name.</summary>
    public IReadOnlyList<string> SkillNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Minimum total years across the selected skills. When no skill is selected the minimum applies
    /// to the candidate's whole skill set.
    /// </summary>
    public int? MinYears { get; set; }

    /// <summary>Free-text search over position, summary and skill names.</summary>
    public string? Keyword { get; set; }

    /// <summary>One-based page number. Values below 1 are treated as 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size. Defaults to 10 and is clamped to 1..50.</summary>
    public int PageSize { get; set; } = 10;
}

/// <summary>A skill shown on a discoverable candidate card, de-duplicated across resumes.</summary>
public sealed record CandidateSearchSkillDto(int SkillId, string Name, int YearsOfExperience);

/// <summary>
/// Public candidate card returned to employers. Deliberately contains no contact details,
/// date of birth, address, social links, user id or application data (contract D12).
/// </summary>
public sealed record CandidateSearchItemDto(
    int CandidateId,
    string DisplayName,
    string? Position,
    string? Summary,
    string? AvatarUrl,
    IReadOnlyList<CandidateSearchSkillDto> Skills,
    int TotalYears,
    bool IsPremium,
    bool IsBoosted);

/// <summary>Skill option offered by the candidate-search filter UI.</summary>
public sealed record CandidateSearchSkillFilterDto(int Id, string Name, string? Category);
