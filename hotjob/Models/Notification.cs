using System.ComponentModel.DataAnnotations;

namespace hotjob.Models;

/// <summary>Values stored in <see cref="Notification.Kind"/>.</summary>
public static class NotificationKinds
{
    /// <summary>A newly published job matched a Premium candidate's best resume.</summary>
    public const string JobAlert = "JobAlert";
}

/// <summary>In-app notification for a user.</summary>
public class Notification
{
    public int Id { get; set; }

    [MaxLength(255)]
    public string UserId { get; set; } = default!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(2000)]
    public string? Content { get; set; }

    /// <summary>Optional category used to filter notifications; null for free-form employer messages.</summary>
    [MaxLength(40)]
    public string? Kind { get; set; }

    /// <summary>Job this notification refers to, when it is about a specific posting.</summary>
    public int? RelatedJobPostingId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User receiving the notification.</summary>
    public Users.ApplicationUser User { get; set; } = default!;

    /// <summary>Related job posting; null when deleted or when the notification is not job-specific.</summary>
    public Job.JobPosting? RelatedJob { get; set; }
}
