namespace hotjob.Models.Resume;

public class Education
{
    public int Id { get; set; }
    public int ResumeId { get; set; }
    public string SchoolName { get; set; } = default!;
    public string? Major { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public double? Gpa { get; set; }
    public string? Description { get; set; }

    public Resume Resume { get; set; } = default!;
}
