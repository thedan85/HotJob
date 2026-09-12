using System.ComponentModel.DataAnnotations;

namespace hotjob.Models;

/// <summary>In-app notification for a user.</summary>
public class Notification
{
    public int Id { get; set; }

    public string UserId { get; set; } = default!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(2000)]
    public string? Content { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User receiving the notification.</summary>
    public Users.ApplicationUser User { get; set; } = default!;
}
