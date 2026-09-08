namespace hotjob.Models.Resume;

public class WorkExperience
{
    public int Id { get; set; }
    public int ResumeId { get; set; }
    public string CompanyName { get; set; } = default!;
    public string? JobTitle { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Description { get; set; }

    public Resume Resume { get; set; } = default!;
}
