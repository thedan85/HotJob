namespace hotjob.Models.Application;

/// <summary>System-written notes on <see cref="ApplicationStatusHistory"/> rows.</summary>
public static class ApplicationHistoryNotes
{
    /// <summary>The employer closed the job, auto-rejecting open applications.</summary>
    public const string JobClosed = "Job closed";

    /// <summary>An administrator removed the job, auto-rejecting open applications.</summary>
    public const string JobRemovedByAdmin = "Job removed by admin";

    /// <summary>True when the rejection came from closing the job, not from a hiring decision.</summary>
    public static bool IsAutoRejection(string? note) =>
        string.Equals(note, JobClosed, StringComparison.Ordinal)
        || string.Equals(note, JobRemovedByAdmin, StringComparison.Ordinal);
}
