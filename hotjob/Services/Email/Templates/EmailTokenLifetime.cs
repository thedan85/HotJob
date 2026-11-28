using System.Globalization;

namespace hotjob.Services.Email.Templates;

/// <summary>
/// Formats the configured token lifetime for human-readable email copy.
/// Callers must pass the value from their options so the email never hardcodes a duration.
/// </summary>
public static class EmailTokenLifetime
{
    public static string Describe(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "The token lifetime must be positive.");

        if (lifetime.TotalDays >= 1 && lifetime.TotalDays == Math.Floor(lifetime.TotalDays))
            return lifetime.TotalDays == 1 ? "1 day" : $"{lifetime.TotalDays:0} days";

        if (lifetime.TotalHours >= 1 && lifetime.TotalHours == Math.Floor(lifetime.TotalHours))
            return lifetime.TotalHours == 1 ? "1 hour" : $"{lifetime.TotalHours:0} hours";

        if (lifetime.TotalMinutes >= 1)
        {
            var minutes = lifetime.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
            return minutes == "1" ? "1 minute" : $"{minutes} minutes";
        }

        var seconds = lifetime.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        return seconds == "1" ? "1 second" : $"{seconds} seconds";
    }
}
