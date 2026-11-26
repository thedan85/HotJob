using hotjob.Data;
using hotjob.DTOs.Company;
using hotjob.Models;
using hotjob.Models.Admin;
using hotjob.Models.Users;
using hotjob.Repositories.Company;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.CompanyTests;

/// <summary>Users report reviews; admins work the report queue.</summary>
public sealed class ReviewReportTests
{
    private static global::hotjob.Services.Company.CompanyService CompanyService(TestFixture fx) =>
        new(new CompanyRepository(fx.Db), fx.Db, TimeProvider.System,
            NullLogger<global::hotjob.Services.Company.CompanyService>.Instance);

    private sealed record Seeded(CompanyProfile Company, Candidate Author, Review Review);

    private static async Task<Seeded> SeedReviewAsync(TestFixture fx, string authorEmail = "author@example.com")
    {
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var (author, _) = await MatchTestData.CreateCandidateAsync(fx, authorEmail);
        var review = new Review { CompanyProfileId = company.Id, CandidateId = author.Id, RatingScore = 1, ReviewText = "Awful" };
        fx.Db.Reviews.Add(review);
        await fx.Db.SaveChangesAsync();
        return new Seeded(company, author, review);
    }

    private static ReportReviewRequest Spam() => new() { Reason = ReviewReportReason.Spam };

    [Fact]
    public async Task SignedInUser_ReportsOnce_SecondReportIsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seeded = await SeedReviewAsync(fx);
        var reporter = await fx.SeedUserAsync("reporter@example.com", role: AppRoles.Employer);
        var service = CompanyService(fx);

        var first = await service.ReportReviewAsync(reporter.Id, seeded.Review.Id,
            new ReportReviewRequest { Reason = ReviewReportReason.PersonalData, Details = "  Phone number of HR  " });
        var second = await service.ReportReviewAsync(reporter.Id, seeded.Review.Id, Spam());

        Assert.True(first.Success, string.Join("; ", first.Errors));
        Assert.Equal(ServiceErrorType.Conflict, second.ErrorType);
        var stored = await fx.Db.ReviewReports.AsNoTracking().SingleAsync();
        Assert.Equal(ReviewReportStatus.Pending, stored.Status);
        Assert.Equal(ReviewReportReason.PersonalData, stored.Reason);
        Assert.Equal("Phone number of HR", stored.Details);
    }

    [Fact]
    public async Task InvalidReports_AreRejected()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seeded = await SeedReviewAsync(fx);
        var reporter = await fx.SeedUserAsync("reporter@example.com");
        var service = CompanyService(fx);

        var anonymous = await service.ReportReviewAsync(null, seeded.Review.Id, Spam());
        var ownReview = await service.ReportReviewAsync(seeded.Author.UserId, seeded.Review.Id, Spam());
        var otherWithoutDetails = await service.ReportReviewAsync(reporter.Id, seeded.Review.Id,
            new ReportReviewRequest { Reason = ReviewReportReason.Other, Details = "  " });
        var unknownReason = await service.ReportReviewAsync(reporter.Id, seeded.Review.Id,
            new ReportReviewRequest { Reason = (ReviewReportReason)99 });
        var missing = await service.ReportReviewAsync(reporter.Id, 999_999, Spam());

        Assert.Equal(ServiceErrorType.Unauthorized, anonymous.ErrorType);
        Assert.Equal(ServiceErrorType.Validation, ownReview.ErrorType);
        Assert.Equal(ServiceErrorType.Validation, otherWithoutDetails.ErrorType);
        Assert.Equal(ServiceErrorType.Validation, unknownReason.ErrorType);
        Assert.Equal(ServiceErrorType.NotFound, missing.ErrorType);
        Assert.Empty(await fx.Db.ReviewReports.ToListAsync());
    }

    [Fact]
    public async Task HiddenReview_CannotBeReported()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seeded = await SeedReviewAsync(fx);
        seeded.Review.IsDeleted = true;
        await fx.Db.SaveChangesAsync();
        var reporter = await fx.SeedUserAsync("reporter@example.com");

        var result = await CompanyService(fx).ReportReviewAsync(reporter.Id, seeded.Review.Id, Spam());

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    private static async Task<string> SignInAdminAsync(TestFixture fx)
    {
        var admin = await fx.SeedUserAsync("moderator@example.com", role: AppRoles.Admin);
        fx.SignInAdminOnCircuit(admin.Id, "moderator@example.com"); // AsyncLocal HttpContext would not survive this helper
        return admin.Id;
    }

    [Fact]
    public async Task ReportQueue_MostReportedFirst_WithPendingCounts()
    {
        await using var fx = await TestFixture.CreateAsync();
        var once = await SeedReviewAsync(fx, "a1@example.com");
        var twice = await SeedReviewAsync(fx, "a2@example.com");
        await SeedReviewAsync(fx, "a3@example.com"); // never reported
        var r1 = await fx.SeedUserAsync("r1@example.com");
        var r2 = await fx.SeedUserAsync("r2@example.com");
        var service = CompanyService(fx);
        Assert.True((await service.ReportReviewAsync(r1.Id, once.Review.Id, Spam())).Success);
        Assert.True((await service.ReportReviewAsync(r1.Id, twice.Review.Id, Spam())).Success);
        Assert.True((await service.ReportReviewAsync(r2.Id, twice.Review.Id, Spam())).Success);

        var queue = await fx.CreateReviewAdminService().ListAsync(reportedOnly: true);

        Assert.Equal(new[] { twice.Review.Id, once.Review.Id }, queue.Data!.Items.Select(item => item.Id));
        Assert.Equal(new[] { 2, 1 }, queue.Data.Items.Select(item => item.PendingReportCount));
    }

    [Fact]
    public async Task HidingAReportedReview_UpholdsAllItsPendingReports()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var seeded = await SeedReviewAsync(fx);
        var r1 = await fx.SeedUserAsync("r1@example.com");
        var r2 = await fx.SeedUserAsync("r2@example.com");
        var service = CompanyService(fx);
        await service.ReportReviewAsync(r1.Id, seeded.Review.Id, Spam());
        await service.ReportReviewAsync(r2.Id, seeded.Review.Id, Spam());

        var hidden = await fx.CreateReviewAdminService().DeleteAsync(seeded.Review.Id, adminId, "Spam");

        Assert.True(hidden.Success, string.Join("; ", hidden.Errors));
        var reports = await fx.Db.ReviewReports.AsNoTracking().ToListAsync();
        Assert.All(reports, report =>
        {
            Assert.Equal(ReviewReportStatus.Upheld, report.Status);
            Assert.Equal(adminId, report.ResolvedByAdminId);
            Assert.NotNull(report.ResolvedAt);
        });
        Assert.Empty((await fx.CreateReviewAdminService().ListAsync(reportedOnly: true)).Data!.Items);
    }

    [Fact]
    public async Task DismissingReports_KeepsReviewVisible_IsAudited_AndCannotRepeat()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var seeded = await SeedReviewAsync(fx);
        var reporter = await fx.SeedUserAsync("r1@example.com");
        await CompanyService(fx).ReportReviewAsync(reporter.Id, seeded.Review.Id, Spam());
        var moderation = fx.CreateReviewAdminService();

        var dismissed = await moderation.DismissReportsAsync(seeded.Review.Id, adminId, "Harsh but honest");
        var again = await moderation.DismissReportsAsync(seeded.Review.Id, adminId);
        var reports = await moderation.GetReportsAsync(seeded.Review.Id);

        Assert.True(dismissed.Success, string.Join("; ", dismissed.Errors));
        Assert.Equal(ServiceErrorType.Conflict, again.ErrorType);
        Assert.False((await fx.Db.Reviews.AsNoTracking().SingleAsync()).IsDeleted);
        var report = Assert.Single(reports.Data!);
        Assert.Equal(nameof(ReviewReportStatus.Dismissed), report.Status);
        Assert.Equal("Harsh but honest", report.ResolutionNote);
        Assert.Equal("r1@example.com", report.ReporterEmail);
        var log = Assert.Single(await fx.Db.AdminAuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(AuditActions.ReviewReportsDismiss, log.Action);
        Assert.Contains("Harsh but honest", log.Detail);
    }
}
