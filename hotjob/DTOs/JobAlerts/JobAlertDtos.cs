namespace hotjob.DTOs.JobAlerts;

/// <summary>One in-app job alert for a Premium candidate.</summary>
public sealed record JobAlertDto(
    int Id,
    int? JobPostingId,
    string Title,
    string? Content,
    bool IsRead,
    DateTime CreatedAt);

/// <summary>
/// Alert feed plus Premium state. Free candidates always receive an empty feed and an
/// upsell message; the gate is applied in the service, never by the page.
/// </summary>
public sealed record JobAlertsPageDto(
    bool IsPremium,
    IReadOnlyList<JobAlertDto> Alerts,
    int UnreadCount,
    string? UpsellMessage);
