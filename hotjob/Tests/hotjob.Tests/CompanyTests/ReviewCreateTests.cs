using hotjob.DTOs.Company;
using hotjob.Models;
using hotjob.Models.Users;
using hotjob.Repositories.Company;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.CompanyTests;

public sealed class ReviewCreateTests
{
    private static global::hotjob.Services.Company.CompanyService CreateService(TestFixture fx) =>
        new(
            new CompanyRepository(fx.Db),
            fx.Db,
            TimeProvider.System,
            NullLogger<global::hotjob.Services.Company.CompanyService>.Instance);

    private static async Task<(Candidate Candidate, CompanyProfile Company)> SeedAsync(TestFixture fx)
    {
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, $"{Guid.NewGuid():N}@example.com");
        var company = await MatchTestData.CreateCompanyAsync(fx);
        await MatchTestData.ApplyToCompanyAsync(fx, candidate, resume, company.Id);
        return (candidate, company);
    }

    [Fact]
    public async Task CreateReview_StoresItOnce()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, company) = await SeedAsync(fx);
        var service = CreateService(fx);

        var created = await service.CreateReviewAsync(
            candidate.UserId,
            company.Id,
            new CreateReviewRequest { RatingScore = 4, ReviewText = "  Solid team.  " });

        Assert.True(created.Success);
        Assert.Equal("Solid team.", created.Data!.ReviewText);

        var duplicate = await service.CreateReviewAsync(
            candidate.UserId,
            company.Id,
            new CreateReviewRequest { RatingScore = 5 });

        Assert.False(duplicate.Success);
        Assert.Equal(ServiceErrorType.Conflict, duplicate.ErrorType);
        Assert.Single(fx.Db.Reviews);
    }

    [Fact]
    public async Task CreateReview_InvalidRating_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, company) = await SeedAsync(fx);

        var result = await CreateService(fx).CreateReviewAsync(
            candidate.UserId,
            company.Id,
            new CreateReviewRequest { RatingScore = 6 });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task CreateReview_NoCandidateProfile_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);

        var result = await CreateService(fx).CreateReviewAsync(
            "missing-user",
            company.Id,
            new CreateReviewRequest { RatingScore = 5 });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
