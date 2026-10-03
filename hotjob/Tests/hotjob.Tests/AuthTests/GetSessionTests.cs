using hotjob.DTOs.Authentication;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class GetSessionTests
{
    [Fact]
    public async Task GetSession_ValidCookie_ReturnsTokens_WithoutRotating()
    {
        await using var fx = await TestFixture.CreateAsync();
        await fx.SeedUserAsync("session@example.com");
        var login = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "session@example.com",
            Password = "Str0ng!Pass"
        });
        Assert.True(login.Success);
        var rawRefresh = login.Data!.RefreshToken;
        var originalHash = fx.AuthServiceHash(rawRefresh);

        var result = await fx.AuthService.GetSessionAsync(rawRefresh);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(rawRefresh, result.Data!.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(result.Data.AccessToken));
        Assert.Equal("session@example.com", result.Data.Email);
        // SeedUserAsync creates no Candidate/Employer row, so no display name exists.
        Assert.Null(result.Data.DisplayName);

        // Read-only: the stored row must not be rotated or revoked.
        var stored = await fx.Db.RefreshTokens.SingleAsync(t => t.TokenHash == originalHash);
        Assert.Null(stored.RevokedAtUtc);
        Assert.Null(stored.ReplacedByTokenHash);
        Assert.Equal(1, await fx.Db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task GetSession_NullOrEmpty_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        Assert.False((await fx.AuthService.GetSessionAsync(null)).Success);
        Assert.False((await fx.AuthService.GetSessionAsync("")).Success);
        Assert.False((await fx.AuthService.GetSessionAsync("   ")).Success);
    }

    [Fact]
    public async Task GetSession_UnknownToken_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.GetSessionAsync("not-a-real-token");

        Assert.False(result.Success);
        Assert.Equal(Services.Common.ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task GetSession_RevokedToken_ReturnsUnauthorized_WithoutRevokingOtherSessions()
    {
        await using var fx = await TestFixture.CreateAsync();
        await fx.SeedUserAsync("revoked-session@example.com");
        var login = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "revoked-session@example.com",
            Password = "Str0ng!Pass"
        });
        var second = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "revoked-session@example.com",
            Password = "Str0ng!Pass"
        });
        var revokedRaw = login.Data!.RefreshToken;
        var keptRaw = second.Data!.RefreshToken;

        var row = await fx.Db.RefreshTokens.SingleAsync(t => t.TokenHash == fx.AuthServiceHash(revokedRaw));
        row.RevokedAtUtc = DateTime.UtcNow;
        row.RevokedReason = "Logout";
        await fx.Db.SaveChangesAsync();

        var result = await fx.AuthService.GetSessionAsync(revokedRaw);

        Assert.False(result.Success);
        Assert.Equal(Services.Common.ServiceErrorType.Unauthorized, result.ErrorType);

        // Read-only: peeking at a revoked token must NOT revoke sibling sessions.
        var kept = await fx.Db.RefreshTokens.SingleAsync(t => t.TokenHash == fx.AuthServiceHash(keptRaw));
        Assert.Null(kept.RevokedAtUtc);
    }

    [Fact]
    public async Task GetSession_ExpiredToken_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        await fx.SeedUserAsync("expired-session@example.com");
        var login = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "expired-session@example.com",
            Password = "Str0ng!Pass"
        });
        var raw = login.Data!.RefreshToken;

        var row = await fx.Db.RefreshTokens.SingleAsync(t => t.TokenHash == fx.AuthServiceHash(raw));
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await fx.Db.SaveChangesAsync();

        var result = await fx.AuthService.GetSessionAsync(raw);

        Assert.False(result.Success);
        Assert.Equal(Services.Common.ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task GetSession_DisabledUser_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("disabled-session@example.com");
        var login = await fx.AuthService.LoginAsync(new LoginRequest
        {
            Email = "disabled-session@example.com",
            Password = "Str0ng!Pass"
        });
        user.IsActive = false;
        await fx.Db.SaveChangesAsync();

        var result = await fx.AuthService.GetSessionAsync(login.Data!.RefreshToken);

        Assert.False(result.Success);
        Assert.Equal(Services.Common.ServiceErrorType.Unauthorized, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("disabled", StringComparison.OrdinalIgnoreCase));
    }
}
