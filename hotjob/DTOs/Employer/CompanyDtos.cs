using System.ComponentModel.DataAnnotations;
using hotjob.Models.Users;

namespace hotjob.DTOs.Employer;

/// <summary>Company profile fields rendered on the employer company page.</summary>
public sealed class CompanyDetailResponse
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Industry { get; set; }
    public string? Location { get; set; }
    public CompanyScale? Scale { get; set; }
    public string? Description { get; set; }
    public CompanyStatus Status { get; set; }
}

/// <summary>One employer row in the company member list.</summary>
public sealed class CompanyMemberDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public string? Email { get; set; }
    public bool IsOwner { get; set; }
    public MembershipStatus MembershipStatus { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
}

/// <summary>Company page payload: profile plus members plus the caller's owner flag.</summary>
public sealed class CompanyPageResponse
{
    public CompanyDetailResponse Company { get; set; } = new();
    public IReadOnlyList<CompanyMemberDto> Members { get; set; } = Array.Empty<CompanyMemberDto>();
    public bool IsOwner { get; set; }

    /// <summary>Member-created jobs waiting for the owner's approval (always 0 for non-owners).</summary>
    public int PendingJobCount { get; set; }
}

/// <summary>A member-created job waiting for the company owner's approval (two-step approval, step 1).</summary>
public sealed class CompanyPendingJobDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? CreatedByEmployerId { get; set; }
    public string? CreatedByName { get; set; }
    public hotjob.Models.Job.JobType JobType { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public hotjob.Models.Job.JobStatus Status { get; set; }
}

/// <summary>Owner rejection of a member-created job.</summary>
public sealed class RejectCompanyJobRequest
{
    [Required(ErrorMessage = "Rejection reason is required."), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Owner-editable company fields; null means no change. Name/status are admin-only.</summary>
public sealed class UpdateCompanyRequest
{
    [Url, MaxLength(2048)]
    public string? Website { get; set; }

    [MaxLength(100)]
    public string? Industry { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    public CompanyScale? Scale { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }
}
