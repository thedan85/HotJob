using hotjob.Data;
using hotjob.Models.Billing;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.BillingTests;

/// <summary>Plan/Payment schema rules: seeding, uniqueness, delete behaviours and the claim-friendly Status token.</summary>
public sealed class PaymentDataModelTests
{
    private static Task SeedPlansAsync(TestFixture fx) =>
        DatabaseSeeder.SeedAsync(fx.Db, NullLogger.Instance);

    private static async Task<Plan> PlanAsync(TestFixture fx, string code) =>
        await fx.Db.Plans.AsNoTracking().SingleAsync(x => x.Code == code);

    private static Payment NewPayment(string payerId, string txnRef, int? companyId = null, int? candidateId = null,
        int? planId = null, int? jobId = null, PaymentPurpose purpose = PaymentPurpose.EmployerPlan) => new()
    {
        PayerUserId = payerId,
        CompanyProfileId = companyId,
        CandidateId = candidateId,
        PlanId = planId,
        JobPostingId = jobId,
        Purpose = purpose,
        Amount = 500_000,
        VnpTxnRef = txnRef,
    };

    private static async Task<(string UserId, CompanyProfile Company)> SeedCompanyAsync(TestFixture fx, string email = "owner@example.com")
    {
        var user = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = $"Co {Guid.NewGuid():N}", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        fx.Db.Employers.Add(new Employer
        {
            UserId = user.Id,
            CompanyProfileId = company.Id,
            FullName = "Owner",
            IsOwner = true,
            MembershipStatus = MembershipStatus.Active
        });
        await fx.Db.SaveChangesAsync();
        return (user.Id, company);
    }

    private static async Task<JobPosting> SeedJobAsync(TestFixture fx, int companyId)
    {
        var job = new JobPosting { CompanyProfileId = companyId, Title = "Dev", Description = "Build", Status = JobStatus.Published };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Seed_CreatesCatalogueWithContractValues()
    {
        await using var fx = await TestFixture.CreateAsync();

        await SeedPlansAsync(fx);

        var plans = await fx.Db.Plans.AsNoTracking().ToDictionaryAsync(x => x.Code);
        Assert.Equal(4, plans.Count);

        var free = plans[Plan.FreeCode];
        Assert.Equal((PlanAudience.Employer, 0m, 1, 0), (free.Audience, free.Price, free.JobQuota, free.DurationDays));
        var starter = plans[Plan.StarterCode];
        Assert.Equal((PlanAudience.Employer, 500_000m, 5, 30), (starter.Audience, starter.Price, starter.JobQuota, starter.DurationDays));
        var growth = plans[Plan.GrowthCode];
        Assert.Equal((PlanAudience.Employer, 1_500_000m, 20, 30), (growth.Audience, growth.Price, growth.JobQuota, growth.DurationDays));
        var premium = plans[Plan.PremiumMonthlyCode];
        Assert.Equal((PlanAudience.Candidate, 99_000m, 0, 30), (premium.Audience, premium.Price, premium.JobQuota, premium.DurationDays));
        Assert.All(plans.Values, p => Assert.True(p.IsActive));
        Assert.Equal(4, plans.Values.Select(p => p.SortOrder).Distinct().Count());
        Assert.True(free.SortOrder < starter.SortOrder && starter.SortOrder < growth.SortOrder);
    }

    [Fact]
    public async Task Seed_RunTwice_DoesNotDuplicatePlans()
    {
        await using var fx = await TestFixture.CreateAsync();

        await SeedPlansAsync(fx);
        await SeedPlansAsync(fx);

        Assert.Equal(4, await fx.Db.Plans.CountAsync());
    }

    [Fact]
    public async Task Seed_DoesNotOverwriteAdminEditedPlan()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedPlansAsync(fx);

        var starter = await fx.Db.Plans.SingleAsync(x => x.Code == Plan.StarterCode);
        starter.Price = 650_000;
        starter.JobQuota = 8;
        starter.Name = "Starter Plus";
        starter.IsActive = false;
        await fx.Db.SaveChangesAsync();

        await SeedPlansAsync(fx);

        var after = await PlanAsync(fx, Plan.StarterCode);
        Assert.Equal(650_000m, after.Price);
        Assert.Equal(8, after.JobQuota);
        Assert.Equal("Starter Plus", after.Name);
        Assert.False(after.IsActive);
    }

    [Fact]
    public async Task Seed_AddsOnlyMissingCodes()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedPlansAsync(fx);
        fx.Db.Plans.Remove(await fx.Db.Plans.SingleAsync(x => x.Code == Plan.PremiumMonthlyCode));
        await fx.Db.SaveChangesAsync();

        await SeedPlansAsync(fx);

        Assert.Equal(4, await fx.Db.Plans.CountAsync());
        Assert.Equal(99_000m, (await PlanAsync(fx, Plan.PremiumMonthlyCode)).Price);
    }

    [Fact]
    public async Task Seed_BackfillsFreePlanForCompaniesWithoutOne_AndKeepsPaidCompanies()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedPlansAsync(fx);
        var starter = await PlanAsync(fx, Plan.StarterCode);
        var expires = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var legacy = new CompanyProfile { CompanyName = "Legacy Co" };
        var paid = new CompanyProfile { CompanyName = "Paid Co", PlanId = starter.Id, PlanExpiresAt = expires };
        fx.Db.CompanyProfiles.AddRange(legacy, paid);
        await fx.Db.SaveChangesAsync();

        await SeedPlansAsync(fx);
        await SeedPlansAsync(fx);

        var free = await PlanAsync(fx, Plan.FreeCode);
        var companies = await fx.Db.CompanyProfiles.AsNoTracking().ToDictionaryAsync(x => x.CompanyName);
        Assert.Equal(free.Id, companies["Legacy Co"].PlanId);
        Assert.Null(companies["Legacy Co"].PlanExpiresAt);
        Assert.Equal(starter.Id, companies["Paid Co"].PlanId);
        Assert.Equal(expires, companies["Paid Co"].PlanExpiresAt);
    }

    [Fact]
    public async Task Plan_DuplicateCode_IsRejected()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedPlansAsync(fx);

        fx.Db.Plans.Add(new Plan { Code = Plan.StarterCode, Name = "Clone", Audience = PlanAudience.Employer });

        await Assert.ThrowsAsync<DbUpdateException>(() => fx.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Payment_DuplicateVnpTxnRef_IsRejected()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, company) = await SeedCompanyAsync(fx);
        fx.Db.Payments.Add(NewPayment(userId, "TXN-1", company.Id));
        await fx.Db.SaveChangesAsync();

        fx.Db.Payments.Add(NewPayment(userId, "TXN-1", company.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => fx.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Payment_RoundTrips_WithPendingDefaultAndAllColumns()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedPlansAsync(fx);
        var (userId, company) = await SeedCompanyAsync(fx);
        var starter = await PlanAsync(fx, Plan.StarterCode);
        var paidAt = new DateTime(2026, 10, 1, 3, 4, 5, DateTimeKind.Utc);

        var payment = NewPayment(userId, "TXN-RT", company.Id, planId: starter.Id);
        payment.VnpTransactionNo = "14000001";
        payment.VnpResponseCode = "00";
        payment.VnpBankCode = "NCB";
        payment.VnpPayDate = "20261001100405";
        payment.Note = "ok";
        payment.PaidAt = paidAt;
        fx.Db.Payments.Add(payment);
        await fx.Db.SaveChangesAsync();

        var loaded = await fx.Db.Payments.AsNoTracking().SingleAsync(x => x.VnpTxnRef == "TXN-RT");
        Assert.Equal(PaymentStatus.Pending, loaded.Status);
        Assert.Equal(PaymentPurpose.EmployerPlan, loaded.Purpose);
        Assert.Equal(500_000m, loaded.Amount);
        Assert.Equal(paidAt, loaded.PaidAt);
        Assert.Equal(("14000001", "00", "NCB", "20261001100405"),
            (loaded.VnpTransactionNo, loaded.VnpResponseCode, loaded.VnpBankCode, loaded.VnpPayDate));
        Assert.Equal(starter.Id, loaded.PlanId);
        Assert.Equal(company.Id, loaded.CompanyProfileId);
        Assert.Null(loaded.CandidateId);
    }

    [Fact]
    public async Task EntitlementColumns_PersistOnCompanyCandidateAndJob()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedPlansAsync(fx);
        var growth = await PlanAsync(fx, Plan.GrowthCode);
        var premium = await PlanAsync(fx, Plan.PremiumMonthlyCode);
        var (_, company) = await SeedCompanyAsync(fx);
        var job = await SeedJobAsync(fx, company.Id);
        var candidateUser = await fx.SeedUserAsync("cand@example.com");
        var candidate = new Candidate { UserId = candidateUser.Id, FullName = "Cand" };
        fx.Db.Candidates.Add(candidate);
        var t = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        company.PlanId = growth.Id;
        company.PlanStartedAt = t;
        company.PlanExpiresAt = t.AddDays(30);
        job.FeaturedUntil = t.AddDays(7);
        candidate.PremiumPlanId = premium.Id;
        candidate.PremiumStartedAt = t;
        candidate.PremiumUntil = t.AddDays(30);
        candidate.BoostUntil = t.AddDays(30);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var c = await fx.Db.CompanyProfiles.Include(x => x.Plan).SingleAsync(x => x.Id == company.Id);
        Assert.Equal(Plan.GrowthCode, c.Plan!.Code);
        Assert.Equal(t.AddDays(30), c.PlanExpiresAt);
        Assert.Equal(t.AddDays(7), (await fx.Db.JobPostings.SingleAsync(x => x.Id == job.Id)).FeaturedUntil);
        var cd = await fx.Db.Candidates.Include(x => x.PremiumPlan).SingleAsync(x => x.Id == candidate.Id);
        Assert.Equal(Plan.PremiumMonthlyCode, cd.PremiumPlan!.Code);
        Assert.Equal((t, t.AddDays(30), t.AddDays(30)), (cd.PremiumStartedAt, cd.PremiumUntil, cd.BoostUntil));
    }

    [Fact]
    public async Task DeletingCompanyWithPayments_IsBlocked_AndHistorySurvives()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, company) = await SeedCompanyAsync(fx);
        fx.Db.Payments.Add(NewPayment(userId, "TXN-C", company.Id));
        await fx.Db.SaveChangesAsync();

        fx.Db.ChangeTracker.Clear();
        var toDelete = await fx.Db.CompanyProfiles.SingleAsync(x => x.Id == company.Id);
        fx.Db.CompanyProfiles.Remove(toDelete);
        await Assert.ThrowsAsync<DbUpdateException>(() => fx.Db.SaveChangesAsync());

        fx.Db.ChangeTracker.Clear();
        Assert.Equal(1, await fx.Db.Payments.CountAsync(x => x.VnpTxnRef == "TXN-C"));
    }

    [Fact]
    public async Task DeletingPayerUser_IsBlocked_AndHistorySurvives()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, company) = await SeedCompanyAsync(fx);
        fx.Db.Payments.Add(NewPayment(userId, "TXN-U", company.Id));
        await fx.Db.SaveChangesAsync();

        var user = await fx.UserManager.FindByIdAsync(userId);
        fx.Db.Employers.RemoveRange(fx.Db.Employers.Where(x => x.UserId == userId));
        fx.Db.Users.Remove(user!);
        await Assert.ThrowsAsync<DbUpdateException>(() => fx.Db.SaveChangesAsync());

        fx.Db.ChangeTracker.Clear();
        Assert.Equal(1, await fx.Db.Payments.CountAsync(x => x.VnpTxnRef == "TXN-U"));
    }

    [Fact]
    public async Task DeletingCandidateWithPayments_IsBlocked_AndHistorySurvives()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("cand@example.com");
        var candidate = new Candidate { UserId = user.Id, FullName = "Cand" };
        fx.Db.Candidates.Add(candidate);
        await fx.Db.SaveChangesAsync();
        fx.Db.Payments.Add(NewPayment(user.Id, "TXN-D", candidateId: candidate.Id, purpose: PaymentPurpose.CandidatePremium));
        await fx.Db.SaveChangesAsync();

        fx.Db.ChangeTracker.Clear();
        fx.Db.Candidates.Remove(candidate);
        await Assert.ThrowsAsync<DbUpdateException>(() => fx.Db.SaveChangesAsync());

        fx.Db.ChangeTracker.Clear();
        Assert.Equal(1, await fx.Db.Payments.CountAsync(x => x.VnpTxnRef == "TXN-D"));
    }

    [Fact]
    public async Task DeletingPurchasedPlan_IsBlocked()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedPlansAsync(fx);
        var (userId, company) = await SeedCompanyAsync(fx);
        var starter = await fx.Db.Plans.SingleAsync(x => x.Code == Plan.StarterCode);
        fx.Db.Payments.Add(NewPayment(userId, "TXN-P", company.Id, planId: starter.Id));
        await fx.Db.SaveChangesAsync();

        fx.Db.ChangeTracker.Clear();
        fx.Db.Plans.Remove(starter);

        await Assert.ThrowsAsync<DbUpdateException>(() => fx.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingFeaturedJob_KeepsPayment_AndDetachesJob()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, company) = await SeedCompanyAsync(fx);
        var job = await SeedJobAsync(fx, company.Id);
        fx.Db.Payments.Add(NewPayment(userId, "TXN-J", company.Id, jobId: job.Id, purpose: PaymentPurpose.EmployerFeatured));
        await fx.Db.SaveChangesAsync();

        fx.Db.JobPostings.Remove(job);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var payment = await fx.Db.Payments.SingleAsync(x => x.VnpTxnRef == "TXN-J");
        Assert.Null(payment.JobPostingId);
        Assert.Equal(company.Id, payment.CompanyProfileId);
    }

    [Fact]
    public async Task Payment_ConcurrentPendingToPaidClaim_OnlyFirstWriterWins()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, company) = await SeedCompanyAsync(fx);
        fx.Db.Payments.Add(NewPayment(userId, "TXN-R", company.Id));
        await fx.Db.SaveChangesAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(fx.Connection).Options;
        await using var first = new ApplicationDbContext(options);
        await using var second = new ApplicationDbContext(options);
        var a = await first.Payments.SingleAsync(x => x.VnpTxnRef == "TXN-R");
        var b = await second.Payments.SingleAsync(x => x.VnpTxnRef == "TXN-R");

        a.Status = PaymentStatus.Paid;
        a.PaidAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        await first.SaveChangesAsync();

        b.Status = PaymentStatus.Paid;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        var stored = await fx.Db.Payments.AsNoTracking().SingleAsync(x => x.VnpTxnRef == "TXN-R");
        Assert.Equal(PaymentStatus.Paid, stored.Status);
        Assert.NotNull(stored.PaidAt);
    }
}
