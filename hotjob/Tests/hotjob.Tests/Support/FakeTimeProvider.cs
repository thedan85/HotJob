namespace hotjob.Tests.Support;

/// <summary>Controllable clock for time-dependent billing rules.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}
