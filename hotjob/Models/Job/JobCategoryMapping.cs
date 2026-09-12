using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Job;

/// <summary>Links a job posting to a category (many-to-many join).</summary>
public class JobCategoryMapping
{
    public int JobPostingId { get; set; }

    public int JobCategoryId { get; set; }

    /// <summary>Posting that belongs to the category.</summary>
    public JobPosting JobPosting { get; set; } = default!;

    /// <summary>Category assigned to the posting.</summary>
    public JobCategory Category { get; set; } = default!;
}
