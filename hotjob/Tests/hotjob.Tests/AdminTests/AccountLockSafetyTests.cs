using System.Security.Claims;
using hotjob.Data;
using hotjob.Models.Users;
using hotjob.Services.Authentication;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests;

/// <summary>
/// Locking accounts: admins cannot lock themselves or other admins, a lock revokes sessions,
/// and disabled users lose access through both the JWT API and open Blazor circuits.
/// </summary>
public sealed class AccountLockSafetyTests
{
    [Fact]
    public async Task Admin_CannotDisableOwnAccount()
    {
        await using var fx = await TestFixture.CreateAsync();
        var admin = await fx.SeedUserAsync("admin@example.com", role: AppRoles.Admin);
        fx.SignInAdmin(admin.Id, admin.Email!);

        var result = await fx.CreateAdminService().ToggleUserActiveAsync(admin.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.True((await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == admin.Id)).IsActive);
        Assert.Empty(await fx.Db.AdminAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Admin_CannotDisableAnotherAdmin()
    {
        await using var fx = await TestFixture.CreateAsync();
        var admin = await fx.SeedUserAsync("admin@example.com", role: AppRoles.Admin);
        var other = await fx.SeedUserAsync("other-admin@example.com", role: AppRoles.Admin);
        fx.SignInAdmin(admin.Id, admin.Email!);

        var result = await fx.CreateAdminService().ToggleUserActiveAsync(other.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.True((await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == other.Id)).IsActive);
        Assert.Empty(await fx.Db.AdminAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Admin_CanReEnableDisabledAdmin()
    {
        await using var fx = await TestFixture.CreateAsync();
        var admin = await fx.SeedUserAsync("admin@example.com", role: AppRoles.Admin);
        var other = await fx.SeedUserAsync("other-admin@example.com", role: AppRoles.Admin);
        other.IsActive = false;
        await fx.Db.SaveChangesAsync();
        fx.SignInAdmin(admin.Id, admin.Email!);

        var result = await fx.CreateAdminService().ToggleUserActiveAsync(other.Id);

        Assert.True(result.Success);
        Assert.True((await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == other.Id)).IsActive);
    }

    [Fact]
    public async Task DisablingUser_RevokesActiveRefreshTokens_AndLeavesOldRevocationsAlone()
    {
        await using var fx = await TestFixture.CreateAsync();
        var admin = await fx.SeedUserAsync("admin@example.com", role: AppRoles.Admin);
        var target = await fx.SeedUserAsync("target@example.com");
        var bystander = await fx.SeedUserAsync("bystander@example.com");
        var now = DateTime.UtcNow;
        var oldRevokedAt = now.AddDays(-1);
        fx.Db.RefreshTokens.AddRange(
            new RefreshToken { TokenHash = "t-active-1", UserId = target.Id, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) },
            new RefreshToken { TokenHash = "t-active-2", UserId = target.Id, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) },
            new RefreshToken
            {
                TokenHash = "t-rotated", UserId = target.Id, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7),
                RevokedAtUtc = oldRevokedAt, RevokedReason = "Rotated"
            },
            new RefreshToken { TokenHash = "b-active", UserId = bystander.Id, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) });
        await fx.Db.SaveChangesAsync();
        fx.SignInAdmin(admin.Id, admin.Email!);

        var result = await fx.CreateAdminService().ToggleUserActiveAsync(target.Id);

        Assert.True(result.Success);
        var tokens = await fx.Db.RefreshTokens.AsNoTracking().ToDictionaryAsync(t => t.TokenHash);
        Assert.Equal("AccountDisabled", tokens["t-active-1"].RevokedReason);
        Assert.Equal("AccountDisabled", tokens["t-active-2"].RevokedReason);
        Assert.NotNull(tokens["t-active-1"].RevokedAtUtc);
        Assert.Equal("Rotated", tokens["t-rotated"].RevokedReason);
        Assert.Null(tokens["b-active"].RevokedAtUtc);
    }

    [Fact]
    public async Task DisabledUser_CannotRefreshOrRestoreSession()
    {
        await using var fx = await TestFixture.CreateAsync();
        var admin = await fx.SeedUserAsync("admin@example.com", role: AppRoles.Admin);
        var target = await fx.SeedUserAsync("target@example.com");
        var login = await fx.AuthService.LoginAsync(
            new hotjob.DTOs.Authentication.LoginRequest { Email = "target@example.com", Password = "Str0ng!Pass" });
        Assert.True(login.Success);
        fx.SignInAdmin(admin.Id, admin.Email!);
        Assert.True((await fx.CreateAdminService().ToggleUserActiveAsync(target.Id)).Success);

        // Re-enable: the old refresh token must stay dead.
        Assert.True((await fx.CreateAdminService().ToggleUserActiveAsync(target.Id)).Success);
        fx.Db.ChangeTracker.Clear();

        Assert.False((await fx.AuthService.GetSessionAsync(login.Data!.RefreshToken)).Success);
        Assert.False((await fx.AuthService.RefreshAsync(login.Data!.RefreshToken)).Success);
    }

    [Fact]
    public async Task JwtValidation_AcceptsActiveUser()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("user@example.com");

        var context = TokenContext(fx, user.Id);
        await ActiveAccountTokenValidation.RejectInactiveAccountAsync(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task JwtValidation_RejectsDisabledUser()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("user@example.com");
        user.IsActive = false;
        await fx.Db.SaveChangesAsync();

        var context = TokenContext(fx, user.Id);
        await ActiveAccountTokenValidation.RejectInactiveAccountAsync(context);

        Assert.NotNull(context.Result?.Failure);
    }

    [Theory]
    [InlineData("missing-user")]
    [InlineData(null)]
    public async Task JwtValidation_RejectsUnknownOrMissingSubject(string? userId)
    {
        await using var fx = await TestFixture.CreateAsync();

        var context = TokenContext(fx, userId);
        await ActiveAccountTokenValidation.RejectInactiveAccountAsync(context);

        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public async Task Circuit_SignsOutDisabledUser()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("user@example.com");
        var (handler, session, provider) = CreateCircuit(fx);
        provider.SignIn(Tokens(user.Id));
        user.IsActive = false;
        await fx.Db.SaveChangesAsync();

        Assert.False(await handler.RevalidateAsync(CancellationToken.None));

        Assert.False(session.IsAuthenticated);
        var state = await provider.GetAuthenticationStateAsync();
        Assert.False(state.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task Circuit_KeepsActiveAndAnonymousSessions()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("user@example.com");
        var (handler, session, provider) = CreateCircuit(fx);

        Assert.True(await handler.RevalidateAsync(CancellationToken.None));

        provider.SignIn(Tokens(user.Id));
        Assert.True(await handler.RevalidateAsync(CancellationToken.None));
        Assert.True(session.IsAuthenticated);
    }

    private static TokenValidatedContext TokenContext(TestFixture fx, string? userId)
    {
        var claims = userId is null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.NameIdentifier, userId)];
        var httpContext = new DefaultHttpContext { RequestServices = fx.Services.CreateScope().ServiceProvider };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestJwt"))
        };
    }

    private static (AccountStatusCircuitHandler Handler, AuthSession Session, HotJobAuthStateProvider Provider)
        CreateCircuit(TestFixture fx)
    {
        var session = new AuthSession();
        var provider = new HotJobAuthStateProvider(session);
        var handler = new AccountStatusCircuitHandler(
            session,
            provider,
            fx.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AccountStatusCircuitHandler>.Instance);
        return (handler, session, provider);
    }

    private static AuthTokens Tokens(string userId) => new(
        "access", DateTime.UtcNow.AddMinutes(60), "refresh", DateTime.UtcNow.AddDays(7),
        userId, "user@example.com", [AppRoles.Candidate]);
}
