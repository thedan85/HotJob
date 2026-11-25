using hotjob.DTOs.Company;
using hotjob.Models;
using hotjob.Models.Users;
using hotjob.Repositories.Company;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.CompanyTests;

/// <summary>P2: candidates submit, validate and view their own company reviews.</summary>
public sealed class CandidateReviewTests
{
    private static global::hotjob.Services.Company.CompanyService CreateService(TestFixture fx) =>
        new(
            new CompanyRepository(fx.Db),
            fx.Db,
            TimeProvider.System,
            NullLogger<global::hotjob.Services.Company.CompanyService>.Instance);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task RatingOutsideOneToFive_IsRejected(int rating)
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "lan@example.com");
        var company = await MatchTestData.CreateCompanyAsync(fx);

        var result = await CreateService(fx).CreateReviewAsync(
            candidate.UserId, company.Id, new CreateReviewRequest { RatingScore = rating });

        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Empty(await fx.Db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task TextOver2000Characters_IsRejected_AndWhitespaceTextIsStoredAsNull()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "lan@example.com");
        var first = await MatchTestData.CreateCompanyAsync(fx);
        var second = await MatchTestData.CreateCompanyAsync(fx);
        var service = CreateService(fx);

        var tooLong = await service.CreateReviewAsync(
            candidate.UserId, first.Id, new CreateReviewRequest { RatingScore = 3, ReviewText = new string('a', 2001) });
        var blank = await service.CreateReviewAsync(
            candidate.UserId, second.Id, new CreateReviewRequest { RatingScore = 3, ReviewText = "   " });

        Assert.Equal(ServiceErrorType.Validation, tooLong.ErrorType);
        Assert.True(blank.Success);
        Assert.Null(blank.Data!.ReviewText);
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task UnapprovedCompany_CannotBeReviewed(CompanyStatus status)
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "lan@example.com");
        var company = new CompanyProfile { CompanyName = "Not yet", Status = status };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var result = await CreateService(fx).CreateReviewAsync(
            candidate.UserId, company.Id, new CreateReviewRequest { RatingScore = 4 });

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task AnonymousUser_CannotReviewOrListReviews()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var service = CreateService(fx);

        var created = await service.CreateReviewAsync(null, company.Id, new CreateReviewRequest { RatingScore = 4 });
        var mine = await service.GetMyReviewsAsync(null);

        Assert.Equal(ServiceErrorType.Unauthorized, created.ErrorType);
        Assert.Equal(ServiceErrorType.Unauthorized, mine.ErrorType);
    }

    [Fact]
    public async Task MyReviews_ListsOnlyTheCallersReviews_NewestFirst_AndFlagsHiddenOnes()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (lan, _) = await MatchTestData.CreateCandidateAsync(fx, "lan@example.com");
        var (minh, _) = await MatchTestData.CreateCandidateAsync(fx, "minh@example.com");
        var older = await MatchTestData.CreateCompanyAsync(fx);
        var newer = await MatchTestData.CreateCompanyAsync(fx);
        fx.Db.Reviews.AddRange(
            new Review { CompanyProfileId = older.Id, CandidateId = lan.Id, RatingScore = 2, CreatedAt = DateTime.UtcNow.AddDays(-3), IsDeleted = true },
            new Review { CompanyProfileId = newer.Id, CandidateId = lan.Id, RatingScore = 5, ReviewText = "Great", CreatedAt = DateTime.UtcNow },
            new Review { CompanyProfileId = newer.Id, CandidateId = minh.Id, RatingScore = 1, ReviewText = "Not mine" });
        await fx.Db.SaveChangesAsync();
        var service = CreateService(fx);

        var mine = await service.GetMyReviewsAsync(lan.UserId);
        var forOneCompany = await service.GetMyReviewsAsync(lan.UserId, older.Id);

        Assert.True(mine.Success);
        Assert.Equal(new[] { newer.Id, older.Id }, mine.Data!.Select(review => review.CompanyId));
        Assert.Equal(new[] { false, true }, mine.Data.Select(review => review.IsHidden));
        Assert.DoesNotContain(mine.Data, review => review.ReviewText == "Not mine");
        Assert.Equal(older.CompanyName, Assert.Single(forOneCompany.Data!).CompanyName);
    }

    [Fact]
    public async Task ReviewHiddenByAModerator_StillCountsAsTheCandidatesOneReview()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "lan@example.com");
        var company = await MatchTestData.CreateCompanyAsync(fx);
        fx.Db.Reviews.Add(new Review { CompanyProfileId = company.Id, CandidateId = candidate.Id, RatingScore = 1, IsDeleted = true });
        await fx.Db.SaveChangesAsync();

        var again = await CreateService(fx).CreateReviewAsync(
            candidate.UserId, company.Id, new CreateReviewRequest { RatingScore = 5 });

        Assert.Equal(ServiceErrorType.Conflict, again.ErrorType);
    }
}
