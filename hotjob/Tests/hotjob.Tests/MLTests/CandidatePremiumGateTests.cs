using hotjob.Models.Billing;
using hotjob.Services.Billing;
using hotjob.Tests.Support;

namespace hotjob.Tests.MLTests;

/// <summary>The server-side Premium gate that protects the advanced match breakdown (contract D10).</summary>
public sealed class CandidatePremiumGateTests
{
    [Fact]
    public async Task GetStatus_ReportsActivePlanExpiryAndBoost_ThenExpiresLazily()
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "status@example.com", ("C#", 1));
        var plan = new Plan
        {
            Audience = PlanAudience.Candidate,
            Code = Plan.PremiumMonthlyCode,
            Name = "HotJob Premium",
            Price = 99_000,
            DurationDays = 30,
            IsActive = true
        };
        fx.Db.Plans.Add(plan);
        await fx.Db.SaveChangesAsync();

        candidate.PremiumPlanId = plan.Id;
        candidate.PremiumUntil = clock.GetUtcNow().AddDays(30).UtcDateTime;
        candidate.BoostUntil = candidate.PremiumUntil;
        await fx.Db.SaveChangesAsync();

        var service = new CandidatePremiumService(fx.Db, clock);
        var active = await service.GetStatusAsync(candidate.UserId);

        Assert.NotNull(active);
        Assert.True(active.IsPremium);
        Assert.Equal("HotJob Premium", active.PlanName);
        Assert.Equal(candidate.PremiumUntil, active.PremiumUntil);
        Assert.Equal(candidate.BoostUntil, active.BoostUntil);
        Assert.True(await service.IsPremiumAsync(candidate.UserId));

        clock.Advance(TimeSpan.FromDays(31));

        Assert.False(await service.IsPremiumAsync(candidate.UserId));
        var expired = await service.GetStatusAsync(candidate.UserId);
        Assert.NotNull(expired);
        Assert.False(expired.IsPremium);
        Assert.Equal("HotJob Premium", expired.PlanName);
    }

    [Fact]
    public async Task IsPremium_IsFalseForGuestsAndUsersWithoutACandidateProfile()
    {
        await using var fx = await TestFixture.CreateAsync();
        var service = new CandidatePremiumService(fx.Db, TimeProvider.System);

        Assert.False(await service.IsPremiumAsync(null));
        Assert.Null(await service.GetStatusAsync("no-such-user"));
    }
}
