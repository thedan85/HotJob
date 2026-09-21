using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

public sealed class RefreshTests
{
    private const string Email = "refresh@example.com";
    private const string Password = "Str0ng!Pass";

    private static async Task<(TestFixture Fx, string RefreshToken)> LoginAsync()
    {
        var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync(Email, Password);
        var login = await fx.AuthService.LoginAsync(new LoginRequest { Email = Email, Password = Password });
        Assert.True(login.Success, string.Join("; ", login.Errors));
        Assert.Equal(user.Id, login.Data!.UserId);
        return (fx, login.Data.RefreshToken);
    }

    [Fact]
    public async Task Refresh_HappyPath_RotatesToken()
    {
        var (fx, oldRefresh) = await LoginAsync();
        await using (fx)
        {
            var result = await fx.AuthService.RefreshAsync(oldRefresh);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            var tokens = result.Data!;
            Assert.NotEqual(oldRefresh, tokens.RefreshToken);
            Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));

            // Old token revoked with link to replacement; new token active.
            var oldHash = fx.AuthServiceHash(oldRefresh);
            var oldRow = await fx.Db.RefreshTokens.SingleAsync(t => t.TokenHash == oldHash);
            Assert.NotNull(oldRow.RevokedAtUtc);
            Assert.Equal("Rotated", oldRow.RevokedReason);
            var newRow = await fx.Db.RefreshTokens.SingleAsync(t => t.TokenHash == fx.AuthServiceHash(tokens.RefreshToken));
            Assert.Null(newRow.RevokedAtUtc);
            Assert.Equal(oldRow.ReplacedByTokenHash, newRow.TokenHash);
        }
    }

    [Fact]
    public async Task Refresh_OldTokenAfterRotation_Rejected_AndRevokesAllSessions()
    {
        var (fx, oldRefresh) = await LoginAsync();
        await using (fx)
        {
            var rotated = await fx.AuthService.RefreshAsync(oldRefresh);
            Assert.True(rotated.Success);

            // Reuse of the already-rotated token: theft response, all sessions gone.
            var reuse = await fx.AuthService.RefreshAsync(oldRefresh);

            Assert.False(reuse.Success);
            Assert.Equal(ServiceErrorType.Unauthorized, reuse.ErrorType);
            Assert.Empty(await fx.Db.RefreshTokens.Where(t => t.RevokedAtUtc == null).ToListAsync());

            // Even the rotated replacement is now dead.
            var dead = await fx.AuthService.RefreshAsync(rotated.Data!.RefreshToken);
            Assert.False(dead.Success);
        }
    }

    [Fact]
    public async Task Refresh_UnknownToken_ReturnsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.RefreshAsync("not-a-real-token");

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refresh_MissingToken_ReturnsUnauthorized(string? token)
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await fx.AuthService.RefreshAsync(token);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ReturnsUnauthorized()
    {
        var (fx, refresh) = await LoginAsync();
        await using (fx)
        {
            var row = await fx.Db.RefreshTokens.SingleAsync(t => t.TokenHash == fx.AuthServiceHash(refresh));
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await fx.Db.SaveChangesAsync();

            var result = await fx.AuthService.RefreshAsync(refresh);

            Assert.False(result.Success);
            Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
            Assert.Contains(result.Errors, e => e.Contains("expired", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Refresh_DisabledAccount_ReturnsUnauthorized()
    {
        var (fx, refresh) = await LoginAsync();
        await using (fx)
        {
            var user = await fx.UserManager.FindByEmailAsync(Email);
            user!.IsActive = false;
            await fx.Db.SaveChangesAsync();

            var result = await fx.AuthService.RefreshAsync(refresh);

            Assert.False(result.Success);
            Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        }
    }

    [Fact]
    public async Task Refresh_LogoutRevokedToken_ReturnsUnauthorized()
    {
        var (fx, refresh) = await LoginAsync();
        await using (fx)
        {
            var user = await fx.UserManager.FindByEmailAsync(Email);
            var logout = await fx.AuthService.LogoutAsync(refresh, user!.Id);
            Assert.True(logout.Success);

            var result = await fx.AuthService.RefreshAsync(refresh);

            // Revoked-but-once-valid token presented again triggers reuse protection.
            Assert.False(result.Success);
            Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        }
    }
}
