using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class RegisterEmployerTests
{
    private static RegisterAccountRequest NewCompanyEmployer(string email, string company = "Acme Corp") => new()
    {
        Email = email,
        Password = "Str0ng!Pass",
        FullName = "Owner Person",
        Position = "Founder",
        AccountType = AccountType.Employer,
        CompanyName = company
    };

    [Fact]
    public async Task RegisterEmployer_NewCompany_HappyPath_OwnerActive()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.Service.RegisterAsync(NewCompanyEmployer("owner@example.com"));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.True(result.Data.HasEmployerProfile);
        Assert.False(result.Data.HasCandidateProfile);
        Assert.NotNull(result.Data.CompanyProfileId);

        var user = await fx.UserManager.FindByEmailAsync("owner@example.com");
        Assert.NotNull(user);
        Assert.True(await fx.UserManager.IsInRoleAsync(user, AppRoles.Employer));

        var employer = await fx.Db.Employers
            .Include(e => e.Company)
            .SingleAsync(e => e.UserId == user.Id);
        Assert.True(employer.IsOwner);
        Assert.Equal(MembershipStatus.Active, employer.MembershipStatus);
        Assert.Equal("Acme Corp", employer.Company.CompanyName);
        Assert.Equal(CompanyStatus.Pending, employer.Company.Status);
        Assert.Equal("Founder", employer.Position);
        Assert.Equal(employer.Company.Id, result.Data.CompanyProfileId);
    }

    [Fact]
    public async Task RegisterEmployer_NullPosition_StoredAsNull()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = NewCompanyEmployer("nopos@example.com");
        req.Position = null;

        var result = await fx.Service.RegisterAsync(req);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var user = await fx.UserManager.FindByEmailAsync("nopos@example.com");
        var employer = await fx.Db.Employers.SingleAsync(e => e.UserId == user!.Id);
        Assert.Null(employer.Position);
    }

    [Fact]
    public async Task RegisterEmployer_JoinExistingCompany_PendingNonOwner()
    {
        await using var fx = await TestFixture.CreateAsync();
        var owner = await fx.Service.RegisterAsync(NewCompanyEmployer("owner2@example.com", "Joinable Inc"));
        Assert.True(owner.Success);
        var companyId = owner.Data!.CompanyProfileId!.Value;

        var joiner = new RegisterAccountRequest
        {
            Email = "joiner@example.com",
            Password = "Str0ng!Pass",
            FullName = "Joiner Person",
            AccountType = AccountType.Employer,
            CompanyProfileId = companyId
        };
        var result = await fx.Service.RegisterAsync(joiner);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(companyId, result.Data!.CompanyProfileId);
        var user = await fx.UserManager.FindByEmailAsync("joiner@example.com");
        var employer = await fx.Db.Employers.SingleAsync(e => e.UserId == user!.Id);
        Assert.False(employer.IsOwner);
        Assert.Equal(MembershipStatus.Pending, employer.MembershipStatus);
    }

    [Fact]
    public async Task RegisterEmployer_JoinMissingCompany_ReturnsNotFound_AndNoUserCreated()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = new RegisterAccountRequest
        {
            Email = "ghost@example.com",
            Password = "Str0ng!Pass",
            FullName = "Ghost Person",
            AccountType = AccountType.Employer,
            CompanyProfileId = 999999
        };

        var result = await fx.Service.RegisterAsync(req);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
        // User creation must be rolled back when the company lookup fails.
        Assert.Null(await fx.UserManager.FindByEmailAsync("ghost@example.com"));
        Assert.Equal(0, await fx.Db.Employers.CountAsync());
    }

    [Fact]
    public async Task RegisterEmployer_JoinRejectedCompany_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.Db.CompanyProfiles.Add(new CompanyProfile { CompanyName = "Banned Co", Status = CompanyStatus.Rejected });
        await fx.Db.SaveChangesAsync();
        var banned = await fx.Db.CompanyProfiles.SingleAsync(c => c.CompanyName == "Banned Co");

        var result = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = "rej@example.com",
            Password = "Str0ng!Pass",
            FullName = "Rej Person",
            AccountType = AccountType.Employer,
            CompanyProfileId = banned.Id
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task RegisterEmployer_JoinDeactivatedCompany_ReturnsConflict_AndNoUserCreated()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.Db.CompanyProfiles.Add(new CompanyProfile
        {
            CompanyName = "Closed Co",
            Status = CompanyStatus.Approved,
            IsActive = false
        });
        await fx.Db.SaveChangesAsync();
        var closed = await fx.Db.CompanyProfiles.SingleAsync(c => c.CompanyName == "Closed Co");

        var result = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = "closed@example.com",
            Password = "Str0ng!Pass",
            FullName = "Closed Person",
            AccountType = AccountType.Employer,
            CompanyProfileId = closed.Id
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Null(await fx.UserManager.FindByEmailAsync("closed@example.com"));
        Assert.Equal(0, await fx.Db.Employers.CountAsync());
    }

    private static RegisterAccountRequest JoinRequest(string email, int companyId) => new()
    {
        Email = email,
        Password = "Str0ng!Pass",
        FullName = "Joiner Person",
        AccountType = AccountType.Employer,
        CompanyProfileId = companyId
    };

    [Fact]
    public async Task RegisterEmployer_PendingQueueFull_ReturnsConflict_AndNoUserCreated()
    {
        await using var fx = await TestFixture.CreateAsync();
        var owner = await fx.Service.RegisterAsync(NewCompanyEmployer("owner@example.com", "Busy Co"));
        var companyId = owner.Data!.CompanyProfileId!.Value;
        var limit = hotjob.Services.Account.AccountService.MaxPendingJoinRequestsPerCompany;
        for (var i = 0; i < limit; i++)
            Assert.True((await fx.Service.RegisterAsync(JoinRequest($"j{i}@example.com", companyId))).Success);

        var result = await fx.Service.RegisterAsync(JoinRequest("overflow@example.com", companyId));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Null(await fx.UserManager.FindByEmailAsync("overflow@example.com"));
        Assert.Equal(limit, await fx.Db.Employers.CountAsync(
            e => e.CompanyProfileId == companyId && e.MembershipStatus == MembershipStatus.Pending));
    }

    [Fact]
    public async Task RegisterEmployer_PendingQueue_OnlyPendingCountsTowardCap()
    {
        await using var fx = await TestFixture.CreateAsync();
        var owner = await fx.Service.RegisterAsync(NewCompanyEmployer("owner@example.com", "Busy Co"));
        var companyId = owner.Data!.CompanyProfileId!.Value;
        var limit = hotjob.Services.Account.AccountService.MaxPendingJoinRequestsPerCompany;
        for (var i = 0; i < limit; i++)
            Assert.True((await fx.Service.RegisterAsync(JoinRequest($"j{i}@example.com", companyId))).Success);

        // Owner resolves one request: the slot is freed.
        var resolved = await fx.Db.Employers.FirstAsync(e => !e.IsOwner && e.CompanyProfileId == companyId);
        resolved.MembershipStatus = MembershipStatus.Rejected;
        await fx.Db.SaveChangesAsync();

        var result = await fx.Service.RegisterAsync(JoinRequest("next@example.com", companyId));

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task RegisterEmployer_PendingQueueFull_DoesNotBlockOtherCompanies()
    {
        await using var fx = await TestFixture.CreateAsync();
        var busy = await fx.Service.RegisterAsync(NewCompanyEmployer("owner@example.com", "Busy Co"));
        var other = await fx.Service.RegisterAsync(NewCompanyEmployer("owner2@example.com", "Quiet Co"));
        var limit = hotjob.Services.Account.AccountService.MaxPendingJoinRequestsPerCompany;
        for (var i = 0; i < limit; i++)
            Assert.True((await fx.Service.RegisterAsync(JoinRequest($"j{i}@example.com", busy.Data!.CompanyProfileId!.Value))).Success);

        var result = await fx.Service.RegisterAsync(JoinRequest("elsewhere@example.com", other.Data!.CompanyProfileId!.Value));

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task RegisterEmployer_DuplicateCompanyName_ReturnsConflict_AndNoUserCreated()
    {
        await using var fx = await TestFixture.CreateAsync();
        var first = await fx.Service.RegisterAsync(NewCompanyEmployer("first@example.com", "DupCo"));
        Assert.True(first.Success);

        var second = await fx.Service.RegisterAsync(NewCompanyEmployer("second@example.com", "DupCo"));

        Assert.False(second.Success);
        Assert.Equal(ServiceErrorType.Conflict, second.ErrorType);
        Assert.Null(await fx.UserManager.FindByEmailAsync("second@example.com"));
        Assert.Equal(1, await fx.Db.CompanyProfiles.CountAsync(c => c.CompanyName == "DupCo"));
    }

    [Fact]
    public async Task RegisterEmployer_DuplicateCompanyName_WhitespaceTrimmed_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        Assert.True((await fx.Service.RegisterAsync(NewCompanyEmployer("a@example.com", "TrimCo"))).Success);

        var result = await fx.Service.RegisterAsync(NewCompanyEmployer("b@example.com", "  TrimCo  "));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task RegisterEmployer_JoinApprovedCompany_Succeeds()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.Db.CompanyProfiles.Add(new CompanyProfile { CompanyName = "Approved Co", Status = CompanyStatus.Approved });
        await fx.Db.SaveChangesAsync();
        var approved = await fx.Db.CompanyProfiles.SingleAsync(c => c.CompanyName == "Approved Co");

        var result = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = "ok@example.com",
            Password = "Str0ng!Pass",
            FullName = "Ok Person",
            AccountType = AccountType.Employer,
            CompanyProfileId = approved.Id
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }
}
