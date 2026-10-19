using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class UpgradeToEmployerTests
{
    private static async Task<ApplicationUser> SeedCandidateAsync(TestFixture fx, string email = "cand@example.com")
    {
        var result = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = email,
            Password = "Str0ng!Pass",
            FullName = "Candidate Person",
            AccountType = AccountType.Candidate
        });
        Assert.True(result.Success, string.Join("; ", result.Errors));
        var user = await fx.UserManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user!;
    }

    [Fact]
    public async Task Upgrade_Candidate_NewCompany_OwnerActive_RoleAdded_CandidateKept()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await SeedCandidateAsync(fx);

        var result = await fx.Service.UpgradeToEmployerAsync(user.Id, new UpgradeToEmployerRequest
        {
            FullName = "Candidate Person",
            Position = "CTO",
            CompanyName = "New Startup"
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Data!.HasEmployerProfile);
        Assert.True(result.Data.HasCandidateProfile);

        Assert.True(await fx.UserManager.IsInRoleAsync(user, AppRoles.Employer));
        Assert.True(await fx.UserManager.IsInRoleAsync(user, AppRoles.Candidate));

        var employer = await fx.Db.Employers.Include(e => e.Company).SingleAsync(e => e.UserId == user.Id);
        Assert.True(employer.IsOwner);
        Assert.Equal(MembershipStatus.Active, employer.MembershipStatus);
        Assert.Equal("CTO", employer.Position);
        Assert.Equal("New Startup", employer.Company.CompanyName);
        Assert.Equal(CompanyStatus.Pending, employer.Company.Status);
    }

    [Fact]
    public async Task Upgrade_Candidate_JoinApprovedCompany_PendingNonOwner()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.Db.CompanyProfiles.Add(new CompanyProfile { CompanyName = "Approved Co", Status = CompanyStatus.Approved });
        await fx.Db.SaveChangesAsync();
        var approved = await fx.Db.CompanyProfiles.SingleAsync(c => c.CompanyName == "Approved Co");
        var user = await SeedCandidateAsync(fx);

        var result = await fx.Service.UpgradeToEmployerAsync(user.Id, new UpgradeToEmployerRequest
        {
            FullName = "Candidate Person",
            CompanyProfileId = approved.Id
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var employer = await fx.Db.Employers.SingleAsync(e => e.UserId == user.Id);
        Assert.False(employer.IsOwner);
        Assert.Equal(MembershipStatus.Pending, employer.MembershipStatus);
        Assert.Equal(approved.Id, employer.CompanyProfileId);
    }

    [Fact]
    public async Task Upgrade_AlreadyEmployer_ReturnsConflict_AndKeepsSingleProfile()
    {
        await using var fx = await TestFixture.CreateAsync();
        var employer = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = "boss@example.com",
            Password = "Str0ng!Pass",
            FullName = "Boss Person",
            AccountType = AccountType.Employer,
            CompanyName = "Boss Co"
        });
        Assert.True(employer.Success);
        var user = await fx.UserManager.FindByEmailAsync("boss@example.com");

        var result = await fx.Service.UpgradeToEmployerAsync(user!.Id, new UpgradeToEmployerRequest
        {
            FullName = "Boss Person",
            CompanyName = "Another Co"
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Equal(1, await fx.Db.Employers.CountAsync(e => e.UserId == user.Id));
        Assert.Equal(0, await fx.Db.CompanyProfiles.CountAsync(c => c.CompanyName == "Another Co"));
    }

    [Fact]
    public async Task Upgrade_MissingCompany_ReturnsNotFound_AndNoEmployerProfile()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await SeedCandidateAsync(fx);

        var result = await fx.Service.UpgradeToEmployerAsync(user.Id, new UpgradeToEmployerRequest
        {
            FullName = "Candidate Person",
            CompanyProfileId = 999999
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
        Assert.Equal(0, await fx.Db.Employers.CountAsync(e => e.UserId == user.Id));
        Assert.False(await fx.UserManager.IsInRoleAsync(user, AppRoles.Employer));
    }

    [Fact]
    public async Task Upgrade_NoCompanyChosen_ReturnsValidation_AndNoEmployerProfile()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await SeedCandidateAsync(fx);

        var result = await fx.Service.UpgradeToEmployerAsync(user.Id, new UpgradeToEmployerRequest
        {
            FullName = "Candidate Person"
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Equal(0, await fx.Db.Employers.CountAsync(e => e.UserId == user.Id));
    }

    [Fact]
    public async Task Upgrade_UnknownUser_ReturnsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.Service.UpgradeToEmployerAsync("does-not-exist", new UpgradeToEmployerRequest
        {
            FullName = "Ghost",
            CompanyName = "Ghost Co"
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
