using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Candidate;

/// <summary>Summary row for one of the signed-in candidate's resumes.</summary>
public sealed record ResumeListItemDto(
    int Id,
    string Title,
    string? Summary,
    string? FileUrl,
    bool IsPrimary,
    DateTime CreatedAt,
    int SkillCount,
    int EducationCount,
    int ExperienceCount);

/// <summary>Full resume of the signed-in candidate, including children.</summary>
public sealed class ResumeDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? FileUrl { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<ResumeEducationItemDto> Educations { get; set; } = Array.Empty<ResumeEducationItemDto>();
    public IReadOnlyList<ResumeWorkExperienceItemDto> WorkExperiences { get; set; } = Array.Empty<ResumeWorkExperienceItemDto>();
    public IReadOnlyList<ResumeSkillItemDto> Skills { get; set; } = Array.Empty<ResumeSkillItemDto>();
}

/// <summary>Education entry returned with a resume.</summary>
public sealed record ResumeEducationItemDto(
    int Id,
    string SchoolName,
    string? Major,
    DateOnly? StartDate,
    DateOnly? EndDate,
    double? Gpa,
    string? Description);

/// <summary>Work experience entry returned with a resume.</summary>
public sealed record ResumeWorkExperienceItemDto(
    int Id,
    string CompanyName,
    string? JobTitle,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Description);

/// <summary>Skill attached to a resume, with the candidate's years of experience.</summary>
public sealed record ResumeSkillItemDto(int SkillId, string SkillName, int YearsOfExperience);

/// <summary>A skill the candidate can attach to a resume.</summary>
public sealed record SkillOptionDto(int Id, string Name, string? Category);

/// <summary>Create/update payload for a resume.</summary>
public sealed class ResumeInput
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Summary { get; set; }

    public List<ResumeEducationInput> Educations { get; set; } = new();

    public List<ResumeWorkExperienceInput> WorkExperiences { get; set; } = new();

    public List<ResumeSkillInput> Skills { get; set; } = new();
}

/// <summary>Education entry supplied when creating or editing a resume.</summary>
public sealed class ResumeEducationInput
{
    [Required, MaxLength(200)]
    public string SchoolName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Major { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [Range(0, 4)]
    public double? Gpa { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }
}

/// <summary>Work experience entry supplied when creating or editing a resume.</summary>
public sealed class ResumeWorkExperienceInput
{
    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? JobTitle { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }
}

/// <summary>Skill supplied when creating or editing a resume.</summary>
public sealed class ResumeSkillInput
{
    [Range(1, int.MaxValue)]
    public int SkillId { get; set; }

    [Range(0, 50)]
    public int YearsOfExperience { get; set; }
}
