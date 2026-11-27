using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support.ResumeParsing;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ResumeAiUsageLimiterTests
{
    [Fact]
    public void PerUserGlobalAndUtcRollover()
    {
        var clock = new ManualTimeProvider(new(2026, 11, 26, 23, 59, 0, TimeSpan.Zero));
        var limiter = new ResumeAiUsageLimiter(clock, Options.Create(new ResumeParsingOptions()));
        for (var i = 0; i < 10; i++)
        {
            using var reservation = limiter.TryReserve($"user-{i / 3}");
            Assert.NotNull(reservation);
            reservation.MarkDispatched();
        }
        Assert.Null(limiter.TryReserve("user-0"));
        Assert.Null(limiter.TryReserve("other"));
        clock.Advance(TimeSpan.FromDays(1));
        Assert.NotNull(limiter.TryReserve("user-0"));
    }
    [Fact]
    public void OldUnsentReservation_DoesNotDecrementNextDay()
    {
        var clock = new ManualTimeProvider(new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero));
        var limiter = new ResumeAiUsageLimiter(clock, Options.Create(new ResumeParsingOptions { AiDailyLimit = 1 }));
        var old = limiter.TryReserve("a")!;
        clock.Advance(TimeSpan.FromDays(1));
        using var current = limiter.TryReserve("a")!;
        current.MarkDispatched();
        old.Dispose();
        old.Dispose();
        Assert.Null(limiter.TryReserve("b"));
    }
    [Fact]
    public void ConcurrentReservations_DoNotOvershoot_AndUnsentReleaseWorks()
    {
        var limiter = new ResumeAiUsageLimiter(TimeProvider.System, Options.Create(new ResumeParsingOptions()));
        var accepted = 0;
        Parallel.For(0, 100, i =>
        {
            using var slot = limiter.TryReserve($"u{i}");
            if (slot is null) return;
            Interlocked.Increment(ref accepted);
            slot.MarkDispatched();
        });
        Assert.Equal(10, accepted);
        var fresh = new ResumeAiUsageLimiter(TimeProvider.System, Options.Create(new ResumeParsingOptions()));
        fresh.TryReserve("a")!.Dispose();
        Assert.NotNull(fresh.TryReserve("a"));
    }
}
