using hotjob.Models.Billing;
using hotjob.Services.Billing;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.BillingTests;

public sealed class CandidatePremiumPlanTests
{
    [Fact]
    public async Task GetPurchasablePlan_ReturnsCurrentDetachedCatalogueValues()
    {
        await using var fx = await TestFixture.CreateAsync();
        var plan = NewPlan();
        fx.Db.Plans.Add(plan);
        await fx.Db.SaveChangesAsync();
        var service = new CandidatePremiumService(fx.Db, TimeProvider.System);

        var actual = await service.GetPurchasablePlanAsync();

        Assert.NotNull(actual);
        Assert.Equal(Plan.PremiumMonthlyCode, actual.Code);
        Assert.Equal("Premium Flex", actual.Name);
        Assert.Equal(149_000m, actual.Price);
        Assert.Equal(45, actual.DurationDays);
        Assert.Equal(EntityState.Detached, fx.Db.Entry(actual).State);

        plan.Name = "Premium Updated";
        plan.Price = 179_000m;
        plan.DurationDays = 60;
        await fx.Db.SaveChangesAsync();
        var updated = await service.GetPurchasablePlanAsync();
        Assert.NotNull(updated);
        Assert.Equal("Premium Updated", updated.Name);
        Assert.Equal(179_000m, updated.Price);
        Assert.Equal(60, updated.DurationDays);
    }

    [Fact]
    public async Task GetPurchasablePlan_ReturnsNullForEmptyCatalogue()
    {
        await using var fx = await TestFixture.CreateAsync();
        var service = new CandidatePremiumService(fx.Db, TimeProvider.System);

        Assert.Null(await service.GetPurchasablePlanAsync());
    }

    [Theory]
    [InlineData(Plan.PremiumMonthlyCode, PlanAudience.Candidate, false, 149000)]
    [InlineData(Plan.PremiumMonthlyCode, PlanAudience.Employer, true, 149000)]
    [InlineData(Plan.PremiumMonthlyCode, PlanAudience.Candidate, true, 0)]
    [InlineData(Plan.PremiumMonthlyCode, PlanAudience.Candidate, true, -1)]
    [InlineData("OTHER_CANDIDATE_PLAN", PlanAudience.Candidate, true, 149000)]
    public async Task GetPurchasablePlan_RejectsUnavailableOrUnrelatedRows(
        string code, PlanAudience audience, bool active, int price)
    {
        await using var fx = await TestFixture.CreateAsync();
        var plan = NewPlan();
        plan.Code = code;
        plan.Audience = audience;
        plan.IsActive = active;
        plan.Price = price;
        fx.Db.Plans.Add(plan);
        await fx.Db.SaveChangesAsync();
        var service = new CandidatePremiumService(fx.Db, TimeProvider.System);

        Assert.Null(await service.GetPurchasablePlanAsync());
    }

    private static Plan NewPlan() => new()
    {
        Code = Plan.PremiumMonthlyCode,
        Audience = PlanAudience.Candidate,
        IsActive = true,
        Name = "Premium Flex",
        Price = 149_000m,
        DurationDays = 45
    };
}
