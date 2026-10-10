using hotjob.Data;
using hotjob.DTOs.Employer;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests;

public sealed class CompanyServiceTests
{
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
        var svc = new CompanyService(fx.Db, NullLogger<CompanyService>.Instance);

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
        var svc = new CompanyService(fx.Db, NullLogger<CompanyService>.Instance);

        var result = await svc.GetPageAsync(pending.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task Update_Owner_UpdatesFields()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (ownerId, _, _) = await SeedCompanyAsync(fx, "owner@example.com");
        var svc = new CompanyService(fx.Db, NullLogger<CompanyService>.Instance);

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
        var svc = new CompanyService(fx.Db, NullLogger<CompanyService>.Instance);

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
        var svc = new CompanyService(fx.Db, NullLogger<CompanyService>.Instance);

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
        var svc = new CompanyService(fx.Db, NullLogger<CompanyService>.Instance);

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
        var svc = new CompanyService(fx.Db, NullLogger<CompanyService>.Instance);

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
        var svc = new CompanyService(fx.Db, NullLogger<CompanyService>.Instance);

        var result = await svc.ApproveMemberAsync(ownerId, foreign.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
