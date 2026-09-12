using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Application;

/// <summary>State of a scheduled interview.</summary>
public enum InterviewStatus
{
    Scheduled,
    Completed,
    Cancelled
}

/// <summary>Interview format.</summary>
public enum InterviewType
{
    Online,
    Onsite,
    Phone
}

/// <summary>Interview scheduled for a job application.</summary>
public class Interview
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }

    public DateTime InterviewDate { get; set; }

    [MaxLength(2048)]
    public string? LocationOrLink { get; set; }

    public InterviewType? InterviewType { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public InterviewStatus Status { get; set; } = InterviewStatus.Scheduled;

    /// <summary>Application this interview belongs to.</summary>
    public Application Application { get; set; } = default!;
}
