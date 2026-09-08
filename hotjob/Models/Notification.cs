namespace hotjob.Models;

public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Content { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Users.ApplicationUser User { get; set; } = default!;
}
