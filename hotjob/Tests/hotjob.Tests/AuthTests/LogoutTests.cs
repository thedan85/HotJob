using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class LogoutTests
{
    private const string Email = "logout@example.com";
    private const string Password = "Str0ng!Pass";

    [Fact]
    public async Task Logout_WithCookieSession_RevokesAllSessions()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync(Email, Password);
        var first = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });
        var second = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });
        Assert.True(first.Success);
        Assert.True(second.Success);

        var result = await fx.AuthService.LogoutAsync(first.Data!.RefreshToken, user.Id);

        Assert.True(result.Success);
        Assert.Empty(await fx.Db.RefreshTokens.Where(t => t.RevokedAtUtc == null).ToListAsync());
        var revoked = await fx.Db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        Assert.Equal(2, revoked.Count);
        Assert.All(revoked, t => Assert.Equal("Logout", t.RevokedReason));

        // Both sessions stay dead afterwards.
        Assert.False((await fx.AuthService.RefreshAsync(first.Data.RefreshToken)).Success);
        Assert.False((await fx.AuthService.RefreshAsync(second.Data!.RefreshToken)).Success);
    }

    [Fact]
    public async Task Logout_WithAccessTokenUserIdOnly_RevokesAllSessions()
    {
        // Simulates cookie already cleared client-side: controller falls back to the JWT identity.
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync(Email, Password);
        var login = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });
        Assert.True(login.Success);

        var result = await fx.AuthService.LogoutAsync(null, user.Id);

        Assert.True(result.Success);
        Assert.Empty(await fx.Db.RefreshTokens.Where(t => t.RevokedAtUtc == null).ToListAsync());
    }

    [Fact]
    public async Task Logout_ResolvesUserFromUnknownCookie()
    {
        // Cookie present but no JWT identity: user resolved from the token row itself.
        await using var fx = await TestFixture.CreateAsync();
        await fx.SeedUserAsync(Email, Password);
        var login = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });
        Assert.True(login.Success);

        var result = await fx.AuthService.LogoutAsync(login.Data!.RefreshToken, null);

        Assert.True(result.Success);
        Assert.Empty(await fx.Db.RefreshTokens.Where(t => t.RevokedAtUtc == null).ToListAsync());
    }

    [Fact]
    public async Task Logout_Unauthenticated_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.LogoutAsync("bogus", null);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task Logout_OnlyRevokesOwnSessions()
    {
        await using var fx = await TestFixture.CreateAsync();
        var alice = await fx.SeedUserAsync("alice@example.com", Password);
        var bob = await fx.SeedUserAsync("bob@example.com", Password);
        var aliceLogin = await fx.AuthService.LoginAsync(new LoginRequest { Email = "alice@example.com", Password = Password });
        var bobLogin = await fx.AuthService.LoginAsync(new LoginRequest { Email = "bob@example.com", Password = Password });
        Assert.True(aliceLogin.Success);
        Assert.True(bobLogin.Success);

        await fx.AuthService.LogoutAsync(aliceLogin.Data!.RefreshToken, alice.Id);

        Assert.Empty(await fx.Db.RefreshTokens.Where(t => t.UserId == alice.Id && t.RevokedAtUtc == null).ToListAsync());
        var bobActive = await fx.Db.RefreshTokens.SingleAsync(t => t.UserId == bob.Id);
        Assert.Null(bobActive.RevokedAtUtc);
        Assert.True((await fx.AuthService.RefreshAsync(bobLogin.Data!.RefreshToken)).Success);
    }
}
