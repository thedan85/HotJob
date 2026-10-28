using System.ComponentModel.DataAnnotations;

namespace hotjob.Services.JobAlerts;

/// <summary>Configuration for job alerts (section <c>JobAlerts</c>).</summary>
public sealed class JobAlertsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "JobAlerts";

    /// <summary>
    /// Minimum best-resume match percentage required to alert a candidate.
    /// </summary>
    [Range(0, 100)]
    public int MinMatchPercent { get; set; } = 60;
}
