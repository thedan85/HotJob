using hotjob.Data;
using hotjob.Models.Application;
using hotjob.Models.Billing;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.AdminTests;

/// <summary>Admin phase 1 (A1.6): monthly report and the extended dashboard numbers.</summary>
public sealed class AdminReportTests
{
    // "Now" is mid-June 2026, so a 3-month report covers April, May and June.
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static DateTime Utc(int month, int day) => new(2026, month, day, 9, 0, 0, DateTimeKind.Utc);

    private static async Task<string> SeedAsync(TestFixture fx)
    {
        var acme = await MatchTestData.CreateCompanyAsync(fx);
        acme.CompanyName = "Acme";
        var globex = await MatchTestData.CreateCompanyAsync(fx);
        globex.CompanyName = "Globex";
        var backend = await MatchTestData.CreateJobAsync(fx, acme.Id, [], title: "Backend");
        var frontend = await MatchTestData.CreateJobAsync(fx, acme.Id, [], title: "Frontend");
        var ops = await MatchTestData.CreateJobAsync(fx, globex.Id, [], title: "Ops");
        backend.CreatedAt = Utc(4, 2);
        frontend.CreatedAt = Utc(6, 1);
        ops.CreatedAt = Utc(1, 1); // outside the range

        var (c1, r1) = await MatchTestData.CreateCandidateAsync(fx, "c1@example.com");
        var (c2, r2) = await MatchTestData.CreateCandidateAsync(fx, "c2@example.com");
        var (c3, r3) = await MatchTestData.CreateCandidateAsync(fx, "c3@example.com");
        var a1 = await MatchTestData.CreateApplicationAsync(fx, backend, r1, c1, ApplicationStatus.Pending);
        var a2 = await MatchTestData.CreateApplicationAsync(fx, backend, r2, c2, ApplicationStatus.Rejected);
        var a3 = await MatchTestData.CreateApplicationAsync(fx, frontend, r3, c3, ApplicationStatus.Pending);
        var a4 = await MatchTestData.CreateApplicationAsync(fx, ops, r1, c1, ApplicationStatus.Pending);
        a1.AppliedAt = Utc(4, 10);
        a2.AppliedAt = Utc(6, 10);
        a3.AppliedAt = Utc(6, 11);
        a4.AppliedAt = Utc(3, 31); // outside the range

        foreach (var user in await fx.Db.Users.ToListAsync())
        {
            user.CreatedAt = user.Email == "c1@example.com" ? Utc(5, 5) : Utc(6, 5);
        }

        var payer = (await fx.Db.Users.FirstAsync()).Id;
        fx.Db.Payments.AddRange(
            new Payment { PayerUserId = payer, VnpTxnRef = "p1", Purpose = PaymentPurpose.EmployerPlan, Amount = 500_000m, Status = PaymentStatus.Paid, PaidAt = Utc(5, 20) },
            new Payment { PayerUserId = payer, VnpTxnRef = "p2", Purpose = PaymentPurpose.CandidatePremium, Amount = 99_000m, Status = PaymentStatus.Paid, PaidAt = Utc(6, 2) },
            new Payment { PayerUserId = payer, VnpTxnRef = "p3", Purpose = PaymentPurpose.EmployerFeatured, Amount = 150_000m, Status = PaymentStatus.Paid, PaidAt = Utc(6, 3) },
            new Payment { PayerUserId = payer, VnpTxnRef = "p4", Purpose = PaymentPurpose.EmployerPlan, Amount = 500_000m, Status = PaymentStatus.Pending },
            new Payment { PayerUserId = payer, VnpTxnRef = "p5", Purpose = PaymentPurpose.EmployerPlan, Amount = 500_000m, Status = PaymentStatus.Failed },
            new Payment { PayerUserId = payer, VnpTxnRef = "p6", Purpose = PaymentPurpose.EmployerPlan, Amount = 900_000m, Status = PaymentStatus.Paid, PaidAt = Utc(2, 1) });
        await fx.Db.SaveChangesAsync();
        return payer;
    }

    [Fact]
    public async Task Report_GroupsByMonth_FillsEmptyMonths_AndCountsPaidOnly()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedAsync(fx);
        var service = new AdminReportService(fx.Db, new FakeTimeProvider(Now));

        var result = await service.GetReportAsync(3);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var report = result.Data!;
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), report.FromUtc);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), report.ToUtc);
        Assert.Equal([4, 5, 6], report.Months.Select(m => m.Month).ToArray());

        Assert.Equal([1, 0, 2], report.Months.Select(m => m.Applications).ToArray());
        Assert.Equal([1, 0, 1], report.Months.Select(m => m.NewJobs).ToArray());
        Assert.Equal([0, 1, 2], report.Months.Select(m => m.NewCandidates).ToArray());
        Assert.Equal([0m, 500_000m, 249_000m], report.Months.Select(m => m.Revenue).ToArray());
        Assert.Equal([0, 1, 2], report.Months.Select(m => m.PaidPayments).ToArray());
        Assert.Equal(150_000m, report.Months[2].RevenueByPurpose[nameof(PaymentPurpose.EmployerFeatured)]);
        Assert.Equal(0m, report.Months[0].RevenueByPurpose[nameof(PaymentPurpose.EmployerPlan)]);

        Assert.Equal(3, report.TotalApplications);
        Assert.Equal(749_000m, report.TotalRevenue);
    }

    [Fact]
    public async Task Report_RanksTopCompaniesAndJobs_InRange()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedAsync(fx);

        var report = (await new AdminReportService(fx.Db, new FakeTimeProvider(Now)).GetReportAsync(3)).Data!;

        var company = Assert.Single(report.TopCompanies); // Globex's only application is out of range
        Assert.Equal(("Acme", 3), (company.Name, company.Count));
        Assert.Equal(
            [("Backend", "Acme", 2), ("Frontend", "Acme", 1)],
            report.TopJobs.Select(j => (j.Name, j.Subtitle, j.Count)).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public async Task Report_RejectsOutOfRangeMonths(int months)
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await new AdminReportService(fx.Db).GetReportAsync(months);

        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Dashboard_AddsApplicationAndRevenueNumbers()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedAsync(fx);
        var service = new AdminService(fx.Db, fx.UserManager, fx.RoleManager, fx.CreateAuditService(), new FakeTimeProvider(Now));

        var dashboard = (await service.GetDashboardAsync()).Data!;

        Assert.Equal(4, dashboard.TotalApplications);
        Assert.Equal(2, dashboard.ApplicationsLast30Days);
        Assert.Equal(
            [("Pending", 3), ("Rejected", 1)],
            dashboard.ApplicationsByStatus!.Select(s => (s.Status, s.Count)).ToArray());
        Assert.Equal(249_000m, dashboard.RevenueThisMonth);
        Assert.Equal(2, dashboard.PaidPaymentsThisMonth);
    }
}
