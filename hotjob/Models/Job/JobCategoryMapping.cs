namespace hotjob.Models.Job;

public class JobCategoryMapping
{
    public int JobPostingId { get; set; }
    public int JobCategoryId { get; set; }

    public JobPosting JobPosting { get; set; } = default!;
    public JobCategory Category { get; set; } = default!;
}
