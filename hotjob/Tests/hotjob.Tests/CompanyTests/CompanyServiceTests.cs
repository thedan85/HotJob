using hotjob.DTOs.Company;
using hotjob.Models.Users;
using hotjob.Repositories.Company;
using hotjob.Services.Company;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests;

public sealed class CompanyServiceTests
{
    private static CompanyService CreateService(TestFixture fx) =>
        new(new CompanyRepository(fx.Db), NullLogger<CompanyService>.Instance);

    private static async Task SeedCompanyAsync(
        TestFixture fx,
        string name,
        CompanyStatus status = CompanyStatus.Approved,
        string? industry = null,
        string? location = null)
    {
        fx.Db.CompanyProfiles.Add(new CompanyProfile
        {
            CompanyName = name,
            Status = status,
            Industry = industry,
            Location = location
        });
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_OnlyApproved_Returned()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "Approved Co");
        await SeedCompanyAsync(fx, "Pending Co", CompanyStatus.Pending);
        await SeedCompanyAsync(fx, "Rejected Co", CompanyStatus.Rejected);

        var result = await CreateService(fx).ListAsync(new CompanyListQuery());

        Assert.True(result.Success);
        var names = result.Data!.Items.Select(c => c.CompanyName).ToList();
        Assert.Contains("Approved Co", names);
        Assert.DoesNotContain("Pending Co", names);
        Assert.DoesNotContain("Rejected Co", names);
    }

    [Fact]
    public async Task List_Search_MatchesNameOnly()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "Acme Software", industry: "Finance");
        await SeedCompanyAsync(fx, "Other Corp", industry: "Acme Software");

        var result = await CreateService(fx).ListAsync(new CompanyListQuery { Search = "acme" });

        Assert.True(result.Success);
        Assert.Single(result.Data!.Items);
        Assert.Equal("Acme Software", result.Data.Items[0].CompanyName);
    }

    [Fact]
    public async Task List_Search_CaseInsensitive()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "Software House");

        var result = await CreateService(fx).ListAsync(new CompanyListQuery { Search = "SOFTWARE" });

        Assert.True(result.Success);
        Assert.Single(result.Data!.Items);
    }

    [Fact]
    public async Task List_IndustryFilter_MultiSelect()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "A", industry: "Software");
        await SeedCompanyAsync(fx, "B", industry: "Finance");
        await SeedCompanyAsync(fx, "C", industry: "Health");

        var result = await CreateService(fx).ListAsync(new CompanyListQuery
        {
            Industries = ["Software", "Finance"]
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Items.Count);
    }

    [Fact]
    public async Task List_Filter_CaseInsensitive_And_Whitespace()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "A", industry: "Software", location: "Hanoi");

        var result = await CreateService(fx).ListAsync(new CompanyListQuery
        {
            Industries = [" software "],
            Locations = ["HANOI"]
        });

        Assert.True(result.Success);
        Assert.Single(result.Data!.Items);
    }

    [Fact]
    public async Task List_LocationAndIndustry_Combined()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "Match", industry: "Software", location: "Hanoi");
        await SeedCompanyAsync(fx, "WrongCity", industry: "Software", location: "Saigon");
        await SeedCompanyAsync(fx, "WrongIndustry", industry: "Finance", location: "Hanoi");

        var result = await CreateService(fx).ListAsync(new CompanyListQuery
        {
            Industries = ["Software"],
            Locations = ["Hanoi"]
        });

        Assert.True(result.Success);
        Assert.Single(result.Data!.Items);
        Assert.Equal("Match", result.Data.Items[0].CompanyName);
    }

    [Fact]
    public async Task List_Paging_FixedPageSize20()
    {
        await using var fx = await TestFixture.CreateAsync();
        for (var i = 0; i < 21; i++)
            await SeedCompanyAsync(fx, $"Company {i:00}");

        var service = CreateService(fx);
        var page1 = await service.ListAsync(new CompanyListQuery { Page = 1 });
        var page2 = await service.ListAsync(new CompanyListQuery { Page = 2 });

        Assert.True(page1.Success);
        Assert.Equal(20, page1.Data!.Items.Count);
        Assert.Equal(20, page1.Data.PageSize);
        Assert.Equal(21, page1.Data.TotalCount);
        Assert.Equal(2, page1.Data.TotalPages);
        Assert.True(page2.Success);
        Assert.Single(page2.Data!.Items);
    }

    [Fact]
    public async Task List_EmptyFilters_NoConstraint()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "A", industry: "Software", location: "Hanoi");
        await SeedCompanyAsync(fx, "B", industry: "Finance", location: "Saigon");

        var result = await CreateService(fx).ListAsync(new CompanyListQuery
        {
            Industries = [],
            Locations = ["  "]
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Items.Count);
    }

    [Fact]
    public async Task FilterOptions_DistinctApprovedOnly()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "A", industry: "Software", location: "Hanoi");
        await SeedCompanyAsync(fx, "B", industry: "Software", location: "Saigon");
        await SeedCompanyAsync(fx, "Pending", CompanyStatus.Pending, "PendingIndustry", "PendingCity");
        await SeedCompanyAsync(fx, "Blank", industry: "  ", location: "");

        var result = await CreateService(fx).GetFilterOptionsAsync();

        Assert.True(result.Success);
        Assert.Equal(["Software"], result.Data!.Industries);
        Assert.Equal(["Hanoi", "Saigon"], result.Data.Locations);
    }

    [Fact]
    public async Task FilterOptions_Whitespace_Deduped()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedCompanyAsync(fx, "A", industry: "Hanoisoft", location: "Hanoi");
        await SeedCompanyAsync(fx, "B", industry: "Hanoisoft", location: " Hanoi ");

        var result = await CreateService(fx).GetFilterOptionsAsync();

        Assert.True(result.Success);
        Assert.Equal(["Hanoi"], result.Data!.Locations);
    }
}
