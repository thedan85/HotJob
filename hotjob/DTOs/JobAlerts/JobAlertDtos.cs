namespace hotjob.DTOs.JobAlerts;

/// <summary>One in-app job alert for a candidate.</summary>
public sealed record JobAlertDto(
    int Id,
    int? JobPostingId,
    string Title,
    string? Content,
    bool IsRead,
    DateTime CreatedAt);

/// <summary>Alert feed for the signed-in candidate.</summary>
public sealed record JobAlertsPageDto(
    IReadOnlyList<JobAlertDto> Alerts,
    int UnreadCount);
