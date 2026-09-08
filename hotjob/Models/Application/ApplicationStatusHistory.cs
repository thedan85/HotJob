namespace hotjob.Models.Application;

public class ApplicationStatusHistory
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public ApplicationStatus Status { get; set; }
    public string? ChangedByUserId { get; set; }
    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public Application Application { get; set; } = default!;
    public Users.ApplicationUser? ChangedBy { get; set; }
}
