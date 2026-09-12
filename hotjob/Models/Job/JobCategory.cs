using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Job;

/// <summary>Job category used to classify postings.</summary>
public class JobCategory
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = default!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Postings assigned to this category.</summary>
    public ICollection<JobCategoryMapping> Mappings { get; set; } = new List<JobCategoryMapping>();
}
