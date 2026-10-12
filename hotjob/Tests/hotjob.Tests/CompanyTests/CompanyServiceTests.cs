using hotjob.DTOs.Company;
using hotjob.Models.Users;
using hotjob.Repositories.Company;
using hotjob.Services.Employer;
using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests;

public sealed class CompanyServiceTests
{
    private static Services.Company.CompanyService CreateService(TestFixture fx) =>
        new(new CompanyRepository(fx.Db), NullLogger<Services.Company.CompanyService>.Instance);

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

public sealed class EmployerCompanyServiceTests
{
    private static global::hotjob.Services.Employer.CompanyService CreateService(TestFixture fx) =>
        new(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

    private static async Task<(string OwnerId, int CompanyId, string OwnerUserId)> SeedCompanyAsync(
        TestFixture fx, string email, int? pendingCount = 0)
    {
        var owner = await fx.SeedUserAsync(email, "Str0ng!Pass", AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = $"Co {email}", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var ownerRow = new Employer
        {
            UserId = owner.Id,
            CompanyProfileId = company.Id,
            FullName = "Owner",
            IsOwner = true,
            MembershipStatus = MembershipStatus.Active
        };
        fx.Db.Employers.Add(ownerRow);
        for (var i = 0; i < pendingCount; i++)
        {
            var pending = await fx.SeedUserAsync($"pending{i}-{email}", "Str0ng!Pass", AppRoles.Employer);
            fx.Db.Employers.Add(new Employer
            {
                UserId = pending.Id,
                CompanyProfileId = company.Id,
                FullName = $"Pending {i}",
                MembershipStatus = MembershipStatus.Pending
            });
        }

        await fx.Db.SaveChangesAsync();
        return (owner.Id, company.Id, owner.Id);
    }

    [Fact]
    public async Task GetPage_Owner_ReturnsCompanyMembersAndOwnerFlag()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerId, _, _) = await SeedCompanyAsync(fx, "owner@example.com", pendingCount: 1);
        var svc = new global::hotjob.Services.Employer.CompanyService(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

        var result = await svc.GetPageAsync(ownerId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.IsOwner);
        Assert.Equal(2, result.Data.Members.Count);
        Assert.True(result.Data.Members[0].IsOwner);
    }

    [Fact]
    public async Task GetPage_PendingMember_BlockedUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (_, _, _) = await SeedCompanyAsync(fx, "owner@example.com", pendingCount: 1);
        var pending = await fx.SeedUserAsync("late@example.com", "Str0ng!Pass", AppRoles.Employer);
        var companyId = fx.Db.CompanyProfiles.First().Id;
        fx.Db.Employers.Add(new Employer
        {
            UserId = pending.Id,
            CompanyProfileId = companyId,
            FullName = "Late",
            MembershipStatus = MembershipStatus.Pending
        });
        await fx.Db.SaveChangesAsync();
        var svc = new global::hotjob.Services.Employer.CompanyService(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

        var result = await svc.GetPageAsync(pending.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task Update_Owner_UpdatesFields()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerId, _, _) = await SeedCompanyAsync(fx, "owner@example.com");
        var svc = new global::hotjob.Services.Employer.CompanyService(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

        var result = await svc.UpdateAsync(ownerId, new UpdateCompanyRequest
        {
            Website = "https://co.example.com",
            Industry = "Retail",
            Location = "HCMC",
            Scale = CompanyScale.From10To49,
            Description = "Hiring."
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("https://co.example.com", result.Data!.Website);
        Assert.Equal(CompanyScale.From10To49, result.Data.Scale);
    }

    [Fact]
    public async Task Update_NonOwner_Blocked()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (_, _, _) = await SeedCompanyAsync(fx, "owner@example.com");
        var member = await fx.SeedUserAsync("member@example.com", "Str0ng!Pass", AppRoles.Employer);
        var companyId = fx.Db.CompanyProfiles.First().Id;
        fx.Db.Employers.Add(new Employer
        {
            UserId = member.Id,
            CompanyProfileId = companyId,
            FullName = "Member",
            MembershipStatus = MembershipStatus.Active
        });
        await fx.Db.SaveChangesAsync();
        var svc = new global::hotjob.Services.Employer.CompanyService(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

        var result = await svc.UpdateAsync(member.Id, new UpdateCompanyRequest { Industry = "X" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task Approve_Pending_Activates()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerId, _, _) = await SeedCompanyAsync(fx, "owner@example.com", pendingCount: 1);
        var pendingId = fx.Db.Employers.Where(e => !e.IsOwner).Select(e => e.Id).First();
        var svc = new global::hotjob.Services.Employer.CompanyService(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

        var result = await svc.ApproveMemberAsync(ownerId, pendingId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(MembershipStatus.Active, result.Data!.MembershipStatus);
    }

    [Fact]
    public async Task Reject_Pending_Rejects()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerId, _, _) = await SeedCompanyAsync(fx, "owner@example.com", pendingCount: 1);
        var pendingId = fx.Db.Employers.Where(e => !e.IsOwner).Select(e => e.Id).First();
        var svc = new global::hotjob.Services.Employer.CompanyService(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

        var result = await svc.RejectMemberAsync(ownerId, pendingId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(MembershipStatus.Rejected, result.Data!.MembershipStatus);
    }

    [Fact]
    public async Task Approve_ActiveMember_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerId, _, _) = await SeedCompanyAsync(fx, "owner@example.com");
        var ownerRowId = fx.Db.Employers.Where(e => e.IsOwner).Select(e => e.Id).First();
        var svc = new global::hotjob.Services.Employer.CompanyService(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

        var result = await svc.ApproveMemberAsync(ownerId, ownerRowId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Transition_ForeignCompanyMember_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerId, _, _) = await SeedCompanyAsync(fx, "owner@example.com");
        var other = await fx.SeedUserAsync("other@example.com", "Str0ng!Pass", AppRoles.Employer);
        var otherCompany = new CompanyProfile { CompanyName = "Other", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(otherCompany);
        await fx.Db.SaveChangesAsync();
        var foreign = new Employer
        {
            UserId = other.Id,
            CompanyProfileId = otherCompany.Id,
            FullName = "Foreign",
            MembershipStatus = MembershipStatus.Pending
        };
        fx.Db.Employers.Add(foreign);
        await fx.Db.SaveChangesAsync();
        var svc = new global::hotjob.Services.Employer.CompanyService(fx.Db, NullLogger<global::hotjob.Services.Employer.CompanyService>.Instance);

        var result = await svc.ApproveMemberAsync(ownerId, foreign.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
