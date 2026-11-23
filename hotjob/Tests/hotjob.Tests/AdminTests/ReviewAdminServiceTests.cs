using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.AdminTests;

public sealed class ReviewAdminServiceTests
{
    private static ReviewAdminService CreateService(TestFixture fx) =>
        new(fx.Db, NullLogger<ReviewAdminService>.Instance);

    /// <summary>Two companies, three candidates, four reviews (one deleted).</summary>
    private static async Task<(int AcmeId, int GlobexId)> SeedReviewsAsync(TestFixture fx)
    {
        var acme = await AdminSeed.CompanyAsync(fx, "Acme Corp", CompanyStatus.Approved);
        var globex = await AdminSeed.CompanyAsync(fx, "Globex", CompanyStatus.Approved);
        var alice = await AdminSeed.CandidateAsync(fx, "alice@example.com", "Alice Nguyen");
        var bob = await AdminSeed.CandidateAsync(fx, "bob@example.com", "Bob Tran");
        var carol = await AdminSeed.CandidateAsync(fx, "carol@example.com", "Carol Le");

        await AdminSeed.ReviewAsync(fx, acme.Id, alice.Id, 5, "Great culture");
        await AdminSeed.ReviewAsync(fx, acme.Id, bob.Id, 2, "Long overtime hours");
        await AdminSeed.ReviewAsync(fx, globex.Id, alice.Id, 4, "Decent pay");
        await AdminSeed.ReviewAsync(fx, globex.Id, carol.Id, 1, "Spam spam", isDeleted: true);
        return (acme.Id, globex.Id);
    }

    [Fact]
    public async Task List_FilterByCompany()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (acmeId, _) = await SeedReviewsAsync(fx);

        var result = await CreateService(fx).ListAsync(companyId: acmeId);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.All(result.Data.Items, r => Assert.Equal(acmeId, r.CompanyProfileId));
    }

    [Fact]
    public async Task List_FilterByIsDeleted()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedReviewsAsync(fx);
        var service = CreateService(fx);

        var deleted = await service.ListAsync(isDeleted: true);
        var visible = await service.ListAsync(isDeleted: false);

        var item = Assert.Single(deleted.Data!.Items);
        Assert.True(item.IsDeleted);
        Assert.Equal("seeded", item.DeletionReason);
        Assert.Equal(3, visible.Data!.TotalCount);
    }

    [Fact]
    public async Task List_FilterByExactRating()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedReviewsAsync(fx);

        var result = await CreateService(fx).ListAsync(rating: 4);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(4, item.RatingScore);
        Assert.Equal("Decent pay", item.ReviewText);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task List_RatingOutOfRange_Validation(int rating)
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).ListAsync(rating: rating);

        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Theory]
    [InlineData("overtime", 1)]   // review text
    [InlineData("alice", 2)]      // candidate name (case-insensitive)
    [InlineData("globex", 2)]     // company name
    [InlineData("nothing-matches", 0)]
    public async Task List_Search_MatchesTextCandidateOrCompany(string search, int expected)
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedReviewsAsync(fx);

        var result = await CreateService(fx).ListAsync(search: search);

        Assert.True(result.Success);
        Assert.Equal(expected, result.Data!.TotalCount);
    }

    [Fact]
    public async Task List_CombinedFilters()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (_, globexId) = await SeedReviewsAsync(fx);

        var result = await CreateService(fx).ListAsync(companyId: globexId, isDeleted: false, search: "alice");

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Alice Nguyen", item.CandidateName);
        Assert.Equal("Globex", item.CompanyName);
    }

    [Fact]
    public async Task Delete_ThenDeleteAgain_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var candidate = await AdminSeed.CandidateAsync(fx, "c@example.com", "Cand");
        var review = await AdminSeed.ReviewAsync(fx, company.Id, candidate.Id, 1, "Rude");
        var service = CreateService(fx);

        var first = await service.DeleteAsync(review.Id, adminId, "  Offensive language ");
        var second = await service.DeleteAsync(review.Id, adminId);

        Assert.True(first.Success, string.Join("; ", first.Errors));
        Assert.True(review.IsDeleted);
        Assert.Equal(adminId, review.DeletedByAdminId);
        Assert.Equal("Offensive language", review.DeletionReason);
        Assert.NotNull(review.DeletedAt);
        Assert.Equal(ServiceErrorType.Conflict, second.ErrorType);
    }

    [Fact]
    public async Task Delete_ReasonTooLong_Validation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var candidate = await AdminSeed.CandidateAsync(fx, "c@example.com", "Cand");
        var review = await AdminSeed.ReviewAsync(fx, company.Id, candidate.Id, 3);

        var result = await CreateService(fx).DeleteAsync(review.Id, adminId, new string('x', 501));

        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.False(review.IsDeleted);
    }

    [Fact]
    public async Task Delete_Missing_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);

        var result = await CreateService(fx).DeleteAsync(123, adminId);

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task Restore_ClearsDeletionFields()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var candidate = await AdminSeed.CandidateAsync(fx, "c@example.com", "Cand");
        var review = await AdminSeed.ReviewAsync(fx, company.Id, candidate.Id, 2, "Meh");
        var service = CreateService(fx);
        Assert.True((await service.DeleteAsync(review.Id, adminId, "Mistake")).Success);

        var result = await service.RestoreAsync(review.Id, adminId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(review.IsDeleted);
        Assert.Null(review.DeletedAt);
        Assert.Null(review.DeletedByAdminId);
        Assert.Null(review.DeletionReason);
    }

    [Fact]
    public async Task Restore_NotDeleted_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var candidate = await AdminSeed.CandidateAsync(fx, "c@example.com", "Cand");
        var review = await AdminSeed.ReviewAsync(fx, company.Id, candidate.Id, 5);

        var result = await CreateService(fx).RestoreAsync(review.Id, adminId);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Restore_Missing_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);

        var result = await CreateService(fx).RestoreAsync(77, adminId);

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetDetail_ReturnsCandidateEmail()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var candidate = await AdminSeed.CandidateAsync(fx, "c@example.com", "Cand");
        var review = await AdminSeed.ReviewAsync(fx, company.Id, candidate.Id, 5, "Good");

        var result = await CreateService(fx).GetDetailAsync(review.Id);

        Assert.True(result.Success);
        Assert.Equal("c@example.com", result.Data!.CandidateEmail);
        Assert.Equal("Co", result.Data.CompanyName);
    }
}
