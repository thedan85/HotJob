using Microsoft.Extensions.Options;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class ResumeAiUsageLimiter(TimeProvider clock, IOptions<ResumeParsingOptions> options)
{
    private readonly object gate = new();
    private DateOnly day;
    private int total;
    private readonly Dictionary<string, int> users = new(StringComparer.Ordinal);
    public ResumeAiReservation? TryReserve(string verifiedUserId)
    {
        lock (gate)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (today != day) { day = today; total = 0; users.Clear(); }
            users.TryGetValue(verifiedUserId, out var count);
            if (total >= Math.Max(0, options.Value.AiDailyLimit) || count >= Math.Max(0, options.Value.AiPerUserDailyLimit)) return null;
            total++;
            users[verifiedUserId] = count + 1;
            return new(this, today, verifiedUserId);
        }
    }
    private void Release(DateOnly date, string user)
    {
        lock (gate)
        {
            if (day != date || !users.TryGetValue(user, out var count)) return;
            total--;
            if (count == 1) users.Remove(user); else users[user] = count - 1;
        }
    }
    internal sealed class ResumeAiReservation(ResumeAiUsageLimiter owner, DateOnly date, string user) : IDisposable
    {
        private int state;
        public void MarkDispatched() => Interlocked.CompareExchange(ref state, 1, 0);
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0) owner.Release(date, user);
        }
    }
}
