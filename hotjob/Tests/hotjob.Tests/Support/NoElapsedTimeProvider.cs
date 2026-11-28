namespace hotjob.Tests.Support;

/// <summary>
/// Clock for parser tests: wall time must not turn a correct redaction into a
/// safety-budget failure when the full suite runs test collections in parallel.
/// </summary>
public sealed class NoElapsedTimeProvider : TimeProvider
{
    public override long GetTimestamp() => 0;
}
