using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Resume;

/// <summary>Work experience entry on a resume.</summary>
public class WorkExperience
{
    public int Id { get; set; }

    public int ResumeId { get; set; }

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = default!;

    [MaxLength(200)]
    public string? JobTitle { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Resume this entry belongs to.</summary>
    public Resume Resume { get; set; } = default!;
}
