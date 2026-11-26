using System.Text;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Account.PasswordRecovery;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.PasswordRecoveryTests;

public sealed class PasswordResetTests
{
    [Fact]
    public async Task IndependentlyDisabledAccount_CannotBeReactivatedByStaleCircuitUser()
    {
        await using var fx = await RecoveryFixture.CreateAsync(new FakeEmailSender());
        var cached = await fx.SeedUserAsync();
        var token = await TokenAsync(fx, cached);
        var stamp = cached.SecurityStamp;
        var hash = cached.PasswordHash;
        await using (var adminScope = fx.Services.CreateAsyncScope())
        {
            var adminDb = adminScope.ServiceProvider.GetRequiredService<hotjob.Data.ApplicationDbContext>();
            var user = await adminDb.Users.SingleAsync(row => row.Id == cached.Id);
            // Match the existing admin toggle: IsActive changes without rotating ConcurrencyStamp.
            user.IsActive = false;
            await adminDb.SaveChangesAsync();
        }
        Assert.True(cached.IsActive);

        var result = await fx.GetService<IPasswordResetService>()
            .ResetPasswordAsync(cached.Email, token, "New123!Pass");

        Assert.False(result.Success);
        Assert.Equal([PasswordResetService.InvalidLink], result.Errors);
        var stored = await fx.Db.Users.AsNoTracking().SingleAsync(row => row.Id == cached.Id);
        Assert.False(stored.IsActive);
        Assert.Equal(stamp, stored.SecurityStamp);
        Assert.Equal(hash, stored.PasswordHash);
    }

    private static async Task<string> TokenAsync(TestFixture fx, ApplicationUser user) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await fx.UserManager.GeneratePasswordResetTokenAsync(user)));

    [Fact]
    public async Task Reset_ChangesPasswordAndStampRevokesAllSessionsAndRejectsReuse()
    {
        await using var fx = await RecoveryFixture.CreateAsync(new FakeEmailSender());
        var user = await fx.SeedUserAsync();
        await fx.SeedUserAsync("other@example.test");
        var first = await fx.AuthService.LoginAsync(new LoginRequest { Email = user.Email!, Password = "Str0ng!Pass" });
        var second = await fx.AuthService.LoginAsync(new LoginRequest { Email = user.Email!, Password = "Str0ng!Pass" });
        var other = await fx.AuthService.LoginAsync(new LoginRequest { Email = "other@example.test", Password = "Str0ng!Pass" });
        var stamp = user.SecurityStamp;
        var token = await TokenAsync(fx, user);
        var service = fx.GetService<IPasswordResetService>();

        var result = await service.ResetPasswordAsync(user.Email, token, "New123!Pass");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        await fx.Db.Entry(user).ReloadAsync();
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.True(await fx.UserManager.CheckPasswordAsync(user, "New123!Pass"));
        Assert.False(await fx.UserManager.CheckPasswordAsync(user, "Str0ng!Pass"));
        Assert.All(await fx.Db.RefreshTokens.Where(row => row.UserId == user.Id).ToListAsync(),
            row => Assert.NotNull(row.RevokedAtUtc));
        Assert.False((await fx.AuthService.RefreshAsync(first.Data!.RefreshToken)).Success);
        Assert.False((await fx.AuthService.GetSessionAsync(second.Data!.RefreshToken)).Success);
        Assert.True((await fx.AuthService.GetSessionAsync(other.Data!.RefreshToken)).Success);
        var reused = await service.ResetPasswordAsync(user.Email, token, "Again123!Pass");
        Assert.False(reused.Success);
        Assert.Equal([PasswordResetService.InvalidLink], reused.Errors);
        Assert.True((await fx.AuthService.LoginAsync(new LoginRequest { Email = user.Email!, Password = "New123!Pass" })).Success);
    }

    [Theory]
    [InlineData("unknown", "anything")]
    [InlineData("active", "%%%")]
    [InlineData("active", "a")]
    [InlineData("active", "")]
    [InlineData("active", "dGhpcyBpcyBub3QgYSByZXNldCB0b2tlbg")]
    [InlineData("disabled", "valid")]
    [InlineData("google", "valid")]
    public async Task IneligibleAndInvalidLinks_ShareOneError(string kind, string suppliedToken)
    {
        await using var fx = await RecoveryFixture.CreateAsync(new FakeEmailSender());
        var email = "user@example.test";
        string token = suppliedToken;
        if (kind != "unknown")
        {
            var user = kind == "google"
                ? new ApplicationUser { Email = email, UserName = email, IsActive = true }
                : await fx.SeedUserAsync(email);
            if (kind == "google") Assert.True((await fx.UserManager.CreateAsync(user)).Succeeded);
            if (suppliedToken == "valid") token = await TokenAsync(fx, user);
            if (kind == "disabled")
            {
                user.IsActive = false;
                Assert.True((await fx.UserManager.UpdateAsync(user)).Succeeded);
            }
        }
        var result = await fx.GetService<IPasswordResetService>().ResetPasswordAsync(email, token, "New123!Pass");
        Assert.False(result.Success);
        Assert.Equal([PasswordResetService.InvalidLink], result.Errors);
    }

    [Fact]
    public async Task ExpiredIdentityToken_IsRejectedWithoutSleeping()
    {
        await using var fx = await RecoveryFixture.CreateAsync(new FakeEmailSender());
        var user = await fx.SeedUserAsync();
        var token = await TokenAsync(fx, user);
        // Exercise real DataProtectionTokenProvider expiration, without a time-sensitive sleep.
        fx.GetService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan = TimeSpan.FromSeconds(-1);
        var result = await fx.GetService<IPasswordResetService>().ResetPasswordAsync(user.Email, token, "New123!Pass");
        Assert.Equal([PasswordResetService.InvalidLink], result.Errors);
        Assert.True(await fx.UserManager.CheckPasswordAsync(user, "Str0ng!Pass"));
    }

    [Fact]
    public async Task PolicyErrors_AreIdentityErrorsAndDoNotConsumeToken()
    {
        await using var fx = await RecoveryFixture.CreateAsync(new FakeEmailSender());
        var user = await fx.SeedUserAsync();
        var token = await TokenAsync(fx, user);
        var service = fx.GetService<IPasswordResetService>();
        var result = await service.ResetPasswordAsync(user.Email, token, "weak");
        Assert.False(result.Success);
        Assert.Contains(fx.UserManager.ErrorDescriber.PasswordRequiresDigit().Description, result.Errors);
        Assert.Contains(fx.UserManager.ErrorDescriber.PasswordRequiresUpper().Description, result.Errors);
        Assert.True((await service.ResetPasswordAsync(user.Email, token, "New123!Pass")).Success);
    }

    [Fact]
    public async Task DisabledAccount_CannotLogInAfterReset()
    {
        await using var fx = await RecoveryFixture.CreateAsync(new FakeEmailSender());
        var user = await fx.SeedUserAsync();
        var token = await TokenAsync(fx, user);
        Assert.True((await fx.GetService<IPasswordResetService>().ResetPasswordAsync(user.Email, token, "New123!Pass")).Success);
        user.IsActive = false;
        Assert.True((await fx.UserManager.UpdateAsync(user)).Succeeded);
        var login = await fx.AuthService.LoginAsync(new LoginRequest { Email = user.Email!, Password = "New123!Pass" });
        Assert.False(login.Success);
        Assert.Contains("Account is disabled.", login.Errors);
    }

    [Fact]
    public async Task RevocationFailure_RollsBackPasswordStampAndSessionsAndAllowsRetryInSameScope()
    {
        var log = new RecordingLogger<PasswordResetService>();
        await using var fx = await RecoveryFixture.CreateAsync(new FakeEmailSender(),
            services => services.AddSingleton<ILogger<PasswordResetService>>(log));
        var user = await fx.SeedUserAsync();
        var stamp = user.SecurityStamp;
        var login = await fx.AuthService.LoginAsync(new LoginRequest { Email = user.Email!, Password = "Str0ng!Pass" });
        var token = await TokenAsync(fx, user);
        await fx.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER block_revocation BEFORE UPDATE ON RefreshTokens
            BEGIN SELECT RAISE(FAIL, 'SENTINEL_TOKEN SENTINEL_PASSWORD'); END;
            """);
        var service = fx.GetService<IPasswordResetService>();
        var result = await service.ResetPasswordAsync(user.Email, token, "New123!Pass");
        Assert.False(result.Success);
        var reloaded = await fx.UserManager.FindByEmailAsync(user.Email!);
        Assert.Equal(stamp, reloaded!.SecurityStamp);
        Assert.True(await fx.UserManager.CheckPasswordAsync(reloaded, "Str0ng!Pass"));
        Assert.True((await fx.AuthService.GetSessionAsync(login.Data!.RefreshToken)).Success);
        Assert.DoesNotContain("SENTINEL", log.Text);
        Assert.All(log.Exceptions, exception => Assert.Null(exception));
        await fx.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER block_revocation;");
        Assert.True((await service.ResetPasswordAsync(user.Email, token, "New123!Pass")).Success);
    }

    [Fact]
    public async Task IndependentContexts_CannotConsumeSameTokenTwice()
    {
        await using var fx = await RecoveryFixture.CreateAsync(new FakeEmailSender());
        var user = await fx.SeedUserAsync();
        var token = await TokenAsync(fx, user);
        await using var firstScope = fx.Services.CreateAsyncScope();
        await using var secondScope = fx.Services.CreateAsyncScope();
        var firstManager = firstScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var secondManager = secondScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var first = await firstManager.FindByIdAsync(user.Id);
        var stale = await secondManager.FindByIdAsync(user.Id);
        var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        // Both contexts read the same stamp before either updates it: deterministic concurrency interleaving.
        Assert.True((await firstManager.ResetPasswordAsync(first!, raw, "First123!Pass")).Succeeded);
        var competing = await secondManager.ResetPasswordAsync(stale!, raw, "Second123!Pass");
        Assert.False(competing.Succeeded);
        Assert.Contains(competing.Errors, error => error.Code == "ConcurrencyFailure");
    }
}
