using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class ProfileServiceTests
{
    [Fact]
    public async Task GetProfile_Candidate_ReturnsUnion()
    {
        await using var fx = await TestFixture.CreateAsync();
        var register = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = "candidate@example.com",
            Password = "Str0ng!Pass",
            FullName = "Candidate",
            AccountType = AccountType.Candidate
        });

        var result = await fx.Service.GetProfileAsync(register.Data!.UserId);

        Assert.True(result.Success);
        Assert.Equal("candidate@example.com", result.Data!.Email);
        Assert.True(result.Data.HasCandidateProfile);
        Assert.False(result.Data.HasEmployerProfile);
        Assert.Equal("Candidate", result.Data.Candidate!.FullName);
    }

    [Fact]
    public async Task Update_Candidate_AllFields_Trims()
    {
        await using var fx = await TestFixture.CreateAsync();
        var register = await RegisterCandidateAsync(fx);

        var result = await fx.Service.UpdateProfileAsync(register.Data!.UserId, new UpdateProfileRequest
        {
            FullName = "  Updated Name  ",
            DateOfBirth = new DateOnly(1990, 1, 2),
            Gender = Gender.Other,
            Address = "  Address  ",
            GithubUrl = "https://github.com/example"
        });

        Assert.True(result.Success);
        Assert.Equal("Updated Name", result.Data!.Candidate!.FullName);
        Assert.Equal("Address", result.Data.Candidate.Address);
        Assert.Equal("https://github.com/example", result.Data.Candidate.GithubUrl);
    }

    [Fact]
    public async Task Update_Email_Duplicate_ReturnsConflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var first = await RegisterCandidateAsync(fx, "first@example.com");
        await RegisterCandidateAsync(fx, "second@example.com");

        var result = await fx.Service.UpdateProfileAsync(first.Data!.UserId, new UpdateProfileRequest
        {
            Email = "second@example.com"
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Update_Employer_Position_Empty_Clears()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("employer@example.com", role: AppRoles.Employer);
        var company = new CompanyProfile { CompanyName = "Company" };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        fx.Db.Employers.Add(new Employer
        {
            UserId = user.Id,
            CompanyProfileId = company.Id,
            FullName = "Employer",
            Position = "Manager"
        });
        await fx.Db.SaveChangesAsync();

        var result = await fx.Service.UpdateProfileAsync(user.Id, new UpdateProfileRequest { Position = "" });

        Assert.True(result.Success);
        Assert.Null(result.Data!.Employer!.Position);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync();

        var result = await fx.Service.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "Wrong123!",
            NewPassword = "New123!Pass"
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task ChangePassword_Ok_RevokesSessions()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync();
        var login = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "user@example.com",
            Password = "Str0ng!Pass"
        });

        var result = await fx.Service.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "Str0ng!Pass",
            NewPassword = "New123!Pass"
        });

        Assert.True(result.Success);
        Assert.True(await fx.Db.RefreshTokens.AllAsync(token => token.RevokedAtUtc != null));
        Assert.NotNull(login.Data);
    }

    private static Task<ServiceResult<RegisteredAccountResponse>> RegisterCandidateAsync(
        TestFixture fx, string email = "candidate@example.com") =>
        fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = email,
            Password = "Str0ng!Pass",
            FullName = "Candidate",
            AccountType = AccountType.Candidate
        });
}
