using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class LoginTests
{
    private const string Email = "login@example.com";
    private const string Password = "Str0ng!Pass";

    [Fact]
    public async Task Login_HappyPath_ReturnsBothTokens_AndPersistsHashOnly()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync(Email, Password);

        var result = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var tokens = result.Data!;
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.NotEqual(tokens.AccessToken, tokens.RefreshToken);
        Assert.Equal(user.Id, tokens.UserId);
        Assert.Equal(Email, tokens.Email);
        Assert.Contains(AppRoles.Candidate, tokens.Roles);
        Assert.True(tokens.AccessTokenExpiresAtUtc > DateTime.UtcNow);
        Assert.True(tokens.RefreshTokenExpiresAtUtc > DateTime.UtcNow.AddDays(6));

        // Only the hash is persisted, never the raw token.
        var stored = await fx.Db.RefreshTokens.SingleAsync(t => t.UserId == user.Id);
        Assert.NotEqual(tokens.RefreshToken, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Null(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task Login_EmailCaseInsensitive_Succeeds()
    {
        await using var fx = await TestFixture.CreateAsync();
        await fx.SeedUserAsync(Email, Password);

        var result = await fx.AuthService.LoginAsync(new LoginRequest { Email = "LOGIN@EXAMPLE.COM", Password = Password });

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized_GenericMessage()
    {
        await using var fx = await TestFixture.CreateAsync();
        await fx.SeedUserAsync(Email, Password);

        var result = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = "Wr0ng!Pass" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("Invalid email or password", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, await fx.Db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsSameGenericMessage_NoEnumeration()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.LoginAsync(new LoginRequest { Email = "nobody@example.com", Password = Password });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("Invalid email or password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_DisabledAccount_ReturnsUnauthorized_NoTokens()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync(Email, Password);
        user.IsActive = false;
        await fx.Db.SaveChangesAsync();

        var result = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("disabled", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, await fx.Db.RefreshTokens.CountAsync());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "Str0ng!Pass")]
    [InlineData("login@example.com", "")]
    public async Task Login_MissingCredentials_ReturnsValidation(string email, string password)
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.LoginAsync(new LoginRequest { Email = email, Password = password });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Login_NullRequest_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.LoginAsync(null!);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Login_MultipleLogins_CreateMultipleSessions()
    {
        await using var fx = await TestFixture.CreateAsync();
        await fx.SeedUserAsync(Email, Password);

        var first = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });
        var second = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.Data!.RefreshToken, second.Data!.RefreshToken);
        Assert.Equal(2, await fx.Db.RefreshTokens.CountAsync(t => t.UserId == first.Data.UserId));
    }
}
