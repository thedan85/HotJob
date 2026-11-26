using hotjob.DTOs.Employer;
using hotjob.Models.Application;

namespace hotjob.DTOs.Candidate;

/// <summary>Query parameters for a candidate's own application list.</summary>
public sealed class MyApplicationsQuery
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;

    /// <summary>Matches job title or company name (case-insensitive).</summary>
    public string? Search { get; set; }

    /// <summary>"active", "offers" or "closed"; anything else means all applications.</summary>
    public string? StatusGroup { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;
}

/// <summary>
/// One page of a candidate's own applications, plus the header counts. The
/// counts cover every application the caller owns, not just the current page.
/// </summary>
public sealed class MyApplicationsPageDto : PagedResult<MyApplicationDto>
{
    public int TotalApplications { get; set; }
    public int InProgressCount { get; set; }
    public int OfferCount { get; set; }
}

/// <summary>One resume the candidate can pick when applying.</summary>
public sealed record ResumeOptionDto(int Id, string Title, bool IsPrimary, string? FileUrl);

/// <summary>
/// Everything the job-detail page needs to render the apply panel:
/// the candidate's resumes plus whether they already applied to this job.
/// </summary>
public sealed class ApplyContextDto
{
    public bool AlreadyApplied { get; set; }

    public int? ApplicationId { get; set; }

    public ApplicationStatus? CurrentStatus { get; set; }

    public DateTime? AppliedAt { get; set; }

    public IReadOnlyList<ResumeOptionDto> Resumes { get; set; } = Array.Empty<ResumeOptionDto>();
}

/// <summary>Result of a successful application.</summary>
public sealed record ApplyResultDto(int ApplicationId, ApplicationStatus Status, DateTime AppliedAt);
