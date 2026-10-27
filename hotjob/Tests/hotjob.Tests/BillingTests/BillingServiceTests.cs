using hotjob.Data;
using hotjob.Models.Billing;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Billing;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.BillingTests;

/// <summary>Checkout creation, callback verification and entitlement grants (contract D4–D6).</summary>
public sealed class BillingServiceTests
{
    private const string Secret = "test-hash-secret";
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        TestFixture Fixture,
        FakeTimeProvider Clock,
        VnPayService VnPay,
        BillingService Billing,
        string UserId,
        CompanyProfile Company) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }

    private static VnPayService NewVnPay(bool configured = true) => new(Options.Create(new VnPayOptions
    {
        TmnCode = configured ? "TESTCODE" : string.Empty,
        HashSecret = configured ? Secret : string.Empty,
        BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
        ReturnUrl = "https://localhost:5001/api/vnpay/return"
    }));

    private static BillingService NewBilling(
        TestFixture fx,
        FakeTimeProvider clock,
        VnPayService vnPay) => new(
        fx.Db,
        vnPay,
        Options.Create(new BillingOptions { FeaturedPrice = 200_000, FeaturedDays = 7 }),
        clock,
        NullLogger<BillingService>.Instance);

    private static async Task<Harness> CreateHarnessAsync(bool configured = true, bool owner = true)
    {
        var fx = await TestFixture.CreateAsync();
        await DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);

        var user = await fx.SeedUserAsync("owner@example.com", "Str0ng!Pass", AppRoles.Employer);
        var company = new CompanyProfile
        {
            CompanyName = $"Co {Guid.NewGuid():N}",
            Status = CompanyStatus.Approved
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        fx.Db.Employers.Add(new Employer
        {
            UserId = user.Id,
            CompanyProfileId = company.Id,
            FullName = "Owner",
            IsOwner = owner,
            MembershipStatus = MembershipStatus.Active
        });
        await fx.Db.SaveChangesAsync();

        var clock = new FakeTimeProvider(Start);
        var vnPay = NewVnPay(configured);
        return new Harness(fx, clock, vnPay, NewBilling(fx, clock, vnPay), user.Id, company);
    }

    private static async Task<Payment> PaymentAsync(Harness h, string txnRef) =>
        await h.Fixture.Db.Payments.AsNoTracking().SingleAsync(p => p.VnpTxnRef == txnRef);

    private static Dictionary<string, string?> Callback(
        string txnRef,
        long amountInCents,
        string responseCode = "00",
        string transactionStatus = "00")
    {
        var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["vnp_TxnRef"] = txnRef,
            ["vnp_Amount"] = amountInCents.ToString(),
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_TransactionNo"] = "14000001",
            ["vnp_BankCode"] = "NCB",
            ["vnp_PayDate"] = "20261027150000"
        };
        query["vnp_SecureHash"] = VnPayService.ComputeHash(VnPayService.BuildSignedPayload(query), Secret);
        return query;
    }

    [Fact]
    public async Task EmployerCheckout_CreatesPendingPaymentWithServerPrice()
    {
        await using var h = await CreateHarnessAsync();

        var result = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "203.0.113.7");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal(500_000, result.Data!.Amount);
        Assert.Equal("EmployerPlan", result.Data.Purpose);
        Assert.Contains("vnp_Amount=50000000", result.Data.PaymentUrl);

        var payment = await PaymentAsync(h, result.Data.TxnRef);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(500_000, payment.Amount);
        Assert.Equal(h.Company.Id, payment.CompanyProfileId);
    }

    [Fact]
    public async Task ValidCallback_PaysPaymentAndActivatesPlan()
    {
        await using var h = await CreateHarnessAsync();
        var checkout = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");

        var result = await h.Billing.ProcessCallbackAsync(
            Callback(checkout.Data!.TxnRef, 50_000_000));

        Assert.Equal("00", result.RspCode);
        Assert.True(result.Granted);

        var payment = await PaymentAsync(h, checkout.Data.TxnRef);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.NotNull(payment.PaidAt);

        var company = await h.Fixture.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == h.Company.Id);
        var plan = await h.Fixture.Db.Plans.AsNoTracking().SingleAsync(p => p.Code == Plan.StarterCode);
        Assert.Equal(plan.Id, company.PlanId);
        Assert.Equal(Start.UtcDateTime, company.PlanStartedAt);
        Assert.Equal(Start.UtcDateTime.AddDays(30), company.PlanExpiresAt);
    }

    [Fact]
    public async Task ReplayedCallback_IsIdempotentAndDoesNotExtendAgain()
    {
        await using var h = await CreateHarnessAsync();
        var checkout = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");
        var callback = Callback(checkout.Data!.TxnRef, 50_000_000);

        var first = await h.Billing.ProcessCallbackAsync(callback);
        var expiryAfterFirst = (await h.Fixture.Db.CompanyProfiles.AsNoTracking()
            .SingleAsync(c => c.Id == h.Company.Id)).PlanExpiresAt;
        var second = await h.Billing.ProcessCallbackAsync(callback);

        Assert.True(first.Granted);
        Assert.Equal("02", second.RspCode);
        Assert.False(second.Granted);
        Assert.Equal(
            expiryAfterFirst,
            (await h.Fixture.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == h.Company.Id)).PlanExpiresAt);
    }

    [Fact]
    public async Task BuyingSameActivePlan_RenewsFromCurrentExpiry()
    {
        await using var h = await CreateHarnessAsync();
        var first = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");
        await h.Billing.ProcessCallbackAsync(Callback(first.Data!.TxnRef, 50_000_000));

        h.Clock.Advance(TimeSpan.FromDays(10));
        var second = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");
        await h.Billing.ProcessCallbackAsync(Callback(second.Data!.TxnRef, 50_000_000));

        var company = await h.Fixture.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == h.Company.Id);
        Assert.Equal(Start.UtcDateTime.AddDays(60), company.PlanExpiresAt);
        Assert.Equal(Start.UtcDateTime, company.PlanStartedAt);
    }

    [Fact]
    public async Task BuyingDifferentPlan_ReplacesImmediately()
    {
        await using var h = await CreateHarnessAsync();
        var first = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");
        await h.Billing.ProcessCallbackAsync(Callback(first.Data!.TxnRef, 50_000_000));

        h.Clock.Advance(TimeSpan.FromDays(10));
        var second = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.GrowthCode, "127.0.0.1");
        await h.Billing.ProcessCallbackAsync(Callback(second.Data!.TxnRef, 150_000_000));

        var company = await h.Fixture.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == h.Company.Id);
        var growth = await h.Fixture.Db.Plans.AsNoTracking().SingleAsync(p => p.Code == Plan.GrowthCode);
        Assert.Equal(growth.Id, company.PlanId);
        Assert.Equal(Start.UtcDateTime.AddDays(10), company.PlanStartedAt);
        Assert.Equal(Start.UtcDateTime.AddDays(40), company.PlanExpiresAt);
    }

    [Fact]
    public async Task WrongAmount_GrantsNothingAndLeavesPaymentPending()
    {
        await using var h = await CreateHarnessAsync();
        var checkout = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");

        var result = await h.Billing.ProcessCallbackAsync(Callback(checkout.Data!.TxnRef, 1));

        Assert.Equal("04", result.RspCode);
        Assert.False(result.Granted);
        Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(h, checkout.Data.TxnRef)).Status);
        Assert.Null((await h.Fixture.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == h.Company.Id)).PlanId);
    }

    [Fact]
    public async Task FailedGatewayResponse_MarksPaymentFailedWithoutGrant()
    {
        await using var h = await CreateHarnessAsync();
        var checkout = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");

        var result = await h.Billing.ProcessCallbackAsync(
            Callback(checkout.Data!.TxnRef, 50_000_000, responseCode: "24", transactionStatus: "02"));

        Assert.Equal("00", result.RspCode);
        Assert.False(result.Granted);
        var payment = await PaymentAsync(h, checkout.Data.TxnRef);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("24", payment.VnpResponseCode);
    }

    [Fact]
    public async Task InvalidSignature_ChangesNothing()
    {
        await using var h = await CreateHarnessAsync();
        var checkout = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");
        var callback = Callback(checkout.Data!.TxnRef, 50_000_000);
        callback["vnp_Amount"] = "7";

        var result = await h.Billing.ProcessCallbackAsync(callback);

        Assert.Equal("97", result.RspCode);
        Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(h, checkout.Data.TxnRef)).Status);
    }

    [Fact]
    public async Task ExpiredPendingPayment_IsMarkedExpiredAndNotGranted()
    {
        await using var h = await CreateHarnessAsync();
        var checkout = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");
        h.Clock.Advance(TimeSpan.FromMinutes(16));

        var result = await h.Billing.ProcessCallbackAsync(Callback(checkout.Data!.TxnRef, 50_000_000));

        Assert.Equal("00", result.RspCode);
        Assert.False(result.Granted);
        Assert.Equal(PaymentStatus.Expired, (await PaymentAsync(h, checkout.Data.TxnRef)).Status);
    }

    [Fact]
    public async Task NotConfigured_ReturnsFriendlyError()
    {
        await using var h = await CreateHarnessAsync(configured: false);

        var result = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Errors.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Fixture.Db.Payments);
    }

    [Fact]
    public async Task NonOwner_CannotBuy()
    {
        await using var h = await CreateHarnessAsync(owner: false);

        var result = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.StarterCode, "127.0.0.1");

        Assert.False(result.Success);
        Assert.Equal(hotjob.Services.Common.ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task FeaturedCheckout_GrantsSevenDays()
    {
        await using var h = await CreateHarnessAsync();
        var job = new JobPosting
        {
            CompanyProfileId = h.Company.Id,
            Title = "Backend",
            Description = "Build",
            Status = JobStatus.Published
        };
        h.Fixture.Db.JobPostings.Add(job);
        await h.Fixture.Db.SaveChangesAsync();

        var checkout = await h.Billing.CreateFeaturedCheckoutAsync(h.UserId, job.Id, "127.0.0.1");
        var result = await h.Billing.ProcessCallbackAsync(Callback(checkout.Data!.TxnRef, 20_000_000));

        Assert.True(result.Granted);
        var featured = await h.Fixture.Db.JobPostings.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(Start.UtcDateTime.AddDays(7), featured.FeaturedUntil);
    }

    [Fact]
    public async Task CandidatePremiumCheckout_GrantsThirtyDaysAndBoost()
    {
        await using var h = await CreateHarnessAsync();
        var user = await h.Fixture.SeedUserAsync("candidate@example.com", "Str0ng!Pass", AppRoles.Candidate);
        var candidate = new Candidate { UserId = user.Id, FullName = "Candidate" };
        h.Fixture.Db.Candidates.Add(candidate);
        await h.Fixture.Db.SaveChangesAsync();

        var checkout = await h.Billing.CreateCandidatePremiumCheckoutAsync(
            user.Id, Plan.PremiumMonthlyCode, "127.0.0.1");
        var result = await h.Billing.ProcessCallbackAsync(Callback(checkout.Data!.TxnRef, 9_900_000));

        Assert.True(result.Granted);
        var profile = await h.Fixture.Db.Candidates.AsNoTracking().SingleAsync(c => c.Id == candidate.Id);
        Assert.Equal(Start.UtcDateTime, profile.PremiumStartedAt);
        Assert.Equal(Start.UtcDateTime.AddDays(30), profile.PremiumUntil);
        Assert.Equal(profile.PremiumUntil, profile.BoostUntil);
    }

    [Fact]
    public async Task FreePlan_IsNotPurchasable()
    {
        await using var h = await CreateHarnessAsync();

        var result = await h.Billing.CreateEmployerPlanCheckoutAsync(h.UserId, Plan.FreeCode, "127.0.0.1");

        Assert.False(result.Success);
        Assert.Empty(h.Fixture.Db.Payments);
    }
}
