namespace hotjob.Tests.Support.ResumeParsing;

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private long ticks;
    public override DateTimeOffset GetUtcNow() => now;
    public override long GetTimestamp() => ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void SetUtcNow(DateTimeOffset value) => now = value;
    public void Advance(TimeSpan elapsed)
    {
        now += elapsed;
        ticks += elapsed.Ticks;
        foreach (var timer in timers.ToArray()) timer.Fire(ticks);
    }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }
    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private long due = long.MaxValue;
        private TimeSpan period;
        public bool Change(TimeSpan dueTime, TimeSpan repeat)
        {
            due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.ticks + dueTime.Ticks;
            period = repeat;
            return true;
        }
        public void Fire(long timestamp)
        {
            if (due > timestamp) return;
            due = period > TimeSpan.Zero ? timestamp + period.Ticks : long.MaxValue;
            callback(state);
        }
        public void Dispose() => due = long.MaxValue;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
