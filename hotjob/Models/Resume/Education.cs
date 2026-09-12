using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Resume;

/// <summary>Education entry on a resume.</summary>
public class Education
{
    public int Id { get; set; }

    public int ResumeId { get; set; }

    [Required, MaxLength(200)]
    public string SchoolName { get; set; } = default!;

    [MaxLength(200)]
    public string? Major { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [Range(0, 4)]
    public double? Gpa { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Resume this entry belongs to.</summary>
    public Resume Resume { get; set; } = default!;
}
