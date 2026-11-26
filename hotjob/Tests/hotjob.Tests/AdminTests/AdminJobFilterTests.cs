using hotjob.DTOs.Admin;
using hotjob.Models.Job;
using hotjob.Tests.Support;

namespace hotjob.Tests.AdminTests;

/// <summary>Admin phase 1 (A1.5): job list filters by company, featured, removed, status and search.</summary>
public sealed class AdminJobFilterTests
{
    private static async Task<(int AcmeId, int GlobexId)> SeedAsync(TestFixture fx)
    {
        var acme = await MatchTestData.CreateCompanyAsync(fx);
        var globex = await MatchTestData.CreateCompanyAsync(fx);
        var featured = await MatchTestData.CreateJobAsync(fx, acme.Id, [], title: "Acme featured");
        featured.FeaturedUntil = DateTime.UtcNow.AddDays(3);
        var expiredFeature = await MatchTestData.CreateJobAsync(fx, acme.Id, [], title: "Acme old feature");
        expiredFeature.FeaturedUntil = DateTime.UtcNow.AddDays(-1);
        var removed = await MatchTestData.CreateJobAsync(fx, acme.Id, [], status: JobStatus.Closed, title: "Acme removed");
        removed.ModeratedAt = DateTime.UtcNow;
        removed.ModerationReason = "Spam.";
        await MatchTestData.CreateJobAsync(fx, globex.Id, [], status: JobStatus.Draft, title: "Globex draft");
        await fx.Db.SaveChangesAsync();
        return (acme.Id, globex.Id);
    }

    private static async Task<string[]> TitlesAsync(TestFixture fx, AdminJobQuery query)
    {
        var result = await fx.CreateJobAdminService().ListAsync(query);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return result.Data!.Items.Select(job => job.Title).OrderBy(title => title).ToArray();
    }

    [Fact]
    public async Task FilterByCompany()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (acmeId, globexId) = await SeedAsync(fx);

        Assert.Equal(["Acme featured", "Acme old feature", "Acme removed"], await TitlesAsync(fx, new AdminJobQuery { CompanyId = acmeId }));
        Assert.Equal(["Globex draft"], await TitlesAsync(fx, new AdminJobQuery { CompanyId = globexId }));
    }

    [Fact]
    public async Task FilterByFeatured_UsesCurrentTime()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedAsync(fx);

        Assert.Equal(["Acme featured"], await TitlesAsync(fx, new AdminJobQuery { IsFeatured = true }));
        Assert.Equal(3, (await TitlesAsync(fx, new AdminJobQuery { IsFeatured = false })).Length);
    }

    [Fact]
    public async Task FilterByRemoved_AndExposesReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedAsync(fx);

        var removed = await fx.CreateJobAdminService().ListAsync(new AdminJobQuery { IsModerated = true });
        var job = Assert.Single(removed.Data!.Items);
        Assert.Equal("Acme removed", job.Title);
        Assert.Equal("Spam.", job.ModerationReason);
        Assert.Equal(3, (await TitlesAsync(fx, new AdminJobQuery { IsModerated = false })).Length);
    }

    [Fact]
    public async Task FiltersCombine_WithStatusSearchAndPaging()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (acmeId, _) = await SeedAsync(fx);

        Assert.Equal(
            ["Acme featured", "Acme old feature"],
            await TitlesAsync(fx, new AdminJobQuery { CompanyId = acmeId, Status = JobStatus.Published, IsModerated = false }));
        Assert.Equal(["Acme old feature"], await TitlesAsync(fx, new AdminJobQuery { CompanyId = acmeId, Search = "old", IsFeatured = false }));

        var page = await fx.CreateJobAdminService().ListAsync(new AdminJobQuery { CompanyId = acmeId, Page = 2, PageSize = 2 });
        Assert.Equal(3, page.Data!.TotalCount);
        Assert.Single(page.Data.Items);
        Assert.All(page.Data.Items, item => Assert.Equal(acmeId, item.CompanyProfileId));
    }
}
