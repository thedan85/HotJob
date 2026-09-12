namespace hotjob.Models.Application;

public enum InterviewStatus
{
    Scheduled,
    Completed,
    Cancelled
}

public class Interview
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public DateTime InterviewDate { get; set; }
    public string? LocationOrLink { get; set; }
    public string? InterviewType { get; set; }
    public string? Notes { get; set; }
    public InterviewStatus Status { get; set; } = InterviewStatus.Scheduled;

    public Application Application { get; set; } = default!;
}
