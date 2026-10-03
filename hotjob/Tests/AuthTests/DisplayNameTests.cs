using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Tests.Support;

namespace hotjob.Tests;

public sealed class DisplayNameTests
{
    [Fact]
    public async Task Login_CandidateWithProfile_ReturnsCandidateFullName()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("cand-display@example.com");
        fx.Db.Candidates.Add(new Candidate { UserId = user.Id, FullName = "Nguyen Van A" });
        await fx.Db.SaveChangesAsync();

        var result = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "cand-display@example.com",
            Password = "Str0ng!Pass"
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("Nguyen Van A", result.Data!.DisplayName);
    }

    [Fact]
    public async Task Login_EmployerWithProfile_ReturnsEmployerFullName()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("emp-display@example.com", "Str0ng!Pass", AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = "Display Co" };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        fx.Db.Employers.Add(new Employer
        {
            UserId = user.Id,
            CompanyProfileId = company.Id,
            FullName = "Tran Thi B"
        });
        await fx.Db.SaveChangesAsync();

        var result = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "emp-display@example.com",
            Password = "Str0ng!Pass"
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("Tran Thi B", result.Data!.DisplayName);
    }

    [Fact]
    public async Task Login_UserWithoutProfile_ReturnsNullDisplayName()
    {
        await using var fx = await TestFixture.CreateAsync();
        await fx.SeedUserAsync("bare-display@example.com");

        var result = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "bare-display@example.com",
            Password = "Str0ng!Pass"
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Null(result.Data!.DisplayName);
    }
}
