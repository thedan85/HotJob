using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Application;

/// <summary>Records a single status change of a job application.</summary>
public class ApplicationStatusHistory
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }

    public ApplicationStatus Status { get; set; }

    [MaxLength(255)]
    public string? ChangedByUserId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Application this entry belongs to.</summary>
    public Application Application { get; set; } = default!;

    /// <summary>User who changed the status. Null for system changes.</summary>
    public Users.ApplicationUser? ChangedBy { get; set; }
}
