using hotjob.Models.Application;

namespace hotjob.DTOs.Candidate;

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
