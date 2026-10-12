using System.ComponentModel.DataAnnotations;
using hotjob.Models.Job;

namespace hotjob.DTOs.Job;

public sealed class JobSkillInput
{
    [Required]
    public int SkillId { get; set; }

    [Range(0, 10)]
    public double ImportanceWeight { get; set; } = 1.0;
}

public class CreateJobRequest
{
    /// <summary>Example: "Senior .NET Developer".</summary>
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(10000)]
    public string Description { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal? SalaryMin { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? SalaryMax { get; set; }

    [Range(0, 50)]
    public int ExperienceRequired { get; set; }

    public JobType JobType { get; set; } = JobType.FullTime;

    public DateTime? ExpiryDate { get; set; }

    /// <summary>Defaults to Draft when omitted; only Draft or Published accepted (close via DELETE).</summary>
    public JobStatus? Status { get; set; }

    /// <summary>Existing category ids. Example: [1, 2].</summary>
    public List<int> CategoryIds { get; set; } = new();

    /// <summary>Required skills with importance weights 0-10.</summary>
    public List<JobSkillInput> Skills { get; set; } = new();
}

public sealed class UpdateJobRequest : CreateJobRequest
{
}

public sealed class AssignCategoriesRequest
{
    public List<int> CategoryIds { get; set; } = new();
}

public sealed class AssignSkillsRequest
{
    public List<JobSkillInput> Skills { get; set; } = new();
}

public sealed record JobSkillDto(int SkillId, string SkillName, double ImportanceWeight);

public sealed record JobCategoryDto(int Id, string Name);

public sealed record JobDetailResponse(
    int Id,
    int CompanyProfileId,
    string Title,
    string Description,
    decimal? SalaryMin,
    decimal? SalaryMax,
    int ExperienceRequired,
    JobType JobType,
    DateTime? ExpiryDate,
    JobStatus Status,
    string CompanyName,
    IReadOnlyList<JobSkillDto> Skills,
    IReadOnlyList<JobCategoryDto> Categories,
    int ApplicationsCount,
    DateTime CreatedAt,
    string? CompanyLocation);

public sealed record JobListItemResponse(
    int Id,
    string Title,
    decimal? SalaryMin,
    decimal? SalaryMax,
    int ExperienceRequired,
    JobType JobType,
    DateTime? ExpiryDate,
    JobStatus Status,
    string CompanyName,
    int SkillsCount,
    IReadOnlyList<string> CategoryNames,
    int ApplicationsCount,
    DateTime CreatedAt);

public sealed record JobDeleteResponse(int Id, bool Closed, JobStatus Status);

/// <summary>Public job card item for /it-jobs browsing (published only).</summary>
public sealed record PublicJobListItemResponse(
    int Id,
    string Title,
    string CompanyName,
    string? CompanyLocation,
    decimal? SalaryMin,
    decimal? SalaryMax,
    JobType JobType,
    DateTime? ExpiryDate,
    DateTime CreatedAt,
    IReadOnlyList<string> Skills);

public sealed class PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
