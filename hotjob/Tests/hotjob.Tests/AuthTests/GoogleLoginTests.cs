using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class GoogleLoginTests
{
    private const string ProviderKey = "google-subject-123";

    [Fact]
    public async Task GoogleLogin_NewEmail_CreatesCandidateUser_AndIssuesTokens()
    {
        await using var fx = await TestFixture.CreateAsync();
        await EnsureCandidateRoleAsync(fx);

        var result = await fx.AuthService.LoginWithGoogleAsync(
            ProviderKey, "new@example.com", "New Person");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var tokens = result.Data!;
        Assert.Contains(AppRoles.Candidate, tokens.Roles);
        Assert.Equal("New Person", tokens.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));

        var user = await fx.UserManager.FindByEmailAsync("new@example.com");
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.True(user.IsActive);

        var linked = await fx.UserManager.FindByLoginAsync("Google", ProviderKey);
        Assert.Equal(user.Id, linked?.Id);

        var candidate = await fx.Db.Candidates.SingleAsync(c => c.UserId == user.Id);
        Assert.Equal("New Person", candidate.FullName);
        Assert.Equal(1, await fx.Db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_ExistingEmail_LinksWithoutChangingUserOrRoles()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("existing@example.com", role: AppRoles.Employer);

        var result = await fx.AuthService.LoginWithGoogleAsync(
            ProviderKey, "existing@example.com", "Existing Name");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(user.Id, result.Data!.UserId);
        Assert.Contains(AppRoles.Employer, result.Data.Roles);
        Assert.DoesNotContain(AppRoles.Candidate, result.Data.Roles);
        Assert.Equal(user.Id, (await fx.UserManager.FindByLoginAsync("Google", ProviderKey))?.Id);
        Assert.Equal(1, await fx.Db.Users.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_LinkedAccount_PasswordLoginStillWorks()
    {
        await using var fx = await TestFixture.CreateAsync();
        const string password = "Str0ng!Pass";
        var user = await fx.SeedUserAsync("both@example.com", password);

        var google = await fx.AuthService.LoginWithGoogleAsync(
            ProviderKey, "both@example.com", "Both Login");
        var passwordLogin = await fx.AuthService.LoginAsync(
            new LoginRequest { Email = "both@example.com", Password = password });

        Assert.True(google.Success, string.Join("; ", google.Errors));
        Assert.True(passwordLogin.Success, string.Join("; ", passwordLogin.Errors));
        Assert.Equal(user.Id, google.Data!.UserId);
        Assert.Equal(user.Id, passwordLogin.Data!.UserId);
    }

    [Fact]
    public async Task GoogleLogin_RepeatLogin_ReusesLinkedUser()
    {
        await using var fx = await TestFixture.CreateAsync();
        await EnsureCandidateRoleAsync(fx);

        var first = await fx.AuthService.LoginWithGoogleAsync(
            ProviderKey, "repeat@example.com", "Repeat User");
        var second = await fx.AuthService.LoginWithGoogleAsync(
            ProviderKey, "repeat@example.com", "Repeat User");

        Assert.True(first.Success, string.Join("; ", first.Errors));
        Assert.True(second.Success, string.Join("; ", second.Errors));
        Assert.Equal(first.Data!.UserId, second.Data!.UserId);
        Assert.Equal(1, await fx.Db.Users.CountAsync());
        Assert.Equal(1, await fx.Db.Candidates.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_DisabledAccount_ReturnsUnauthorized_NoTokens()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("disabled@example.com");
        user.IsActive = false;
        Assert.True((await fx.UserManager.UpdateAsync(user)).Succeeded);

        var result = await fx.AuthService.LoginWithGoogleAsync(
            ProviderKey, "disabled@example.com", "Disabled User");

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        Assert.Equal(0, await fx.Db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_MissingEmail_ReturnsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.LoginWithGoogleAsync(ProviderKey, " ", "No Email");

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Equal(0, await fx.Db.Users.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_MissingProviderKey_ReturnsValidationError()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.LoginWithGoogleAsync(" ", "someone@example.com", "No Key");

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Equal(0, await fx.Db.Users.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_BlankName_FallsBackToEmailLocalPart()
    {
        await using var fx = await TestFixture.CreateAsync();
        await EnsureCandidateRoleAsync(fx);

        var result = await fx.AuthService.LoginWithGoogleAsync(
            ProviderKey, "noname@example.com", "  ");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var user = await fx.UserManager.FindByEmailAsync("noname@example.com");
        var candidate = await fx.Db.Candidates.SingleAsync(c => c.UserId == user!.Id);
        Assert.Equal("noname", candidate.FullName);
    }

    private static async Task EnsureCandidateRoleAsync(TestFixture fx)
    {
        if (!await fx.RoleManager.RoleExistsAsync(AppRoles.Candidate))
        {
            Assert.True((await fx.RoleManager.CreateAsync(new IdentityRole(AppRoles.Candidate))).Succeeded);
        }
    }
}
