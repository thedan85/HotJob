namespace hotjob.Models.Job;

public class JobCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    public ICollection<JobCategoryMapping> Mappings { get; set; } = new List<JobCategoryMapping>();
}
