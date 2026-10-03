using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using hotjob.Data;
using hotjob.Models.Users;
using hotjob.Services.Authentication;
using hotjob.Tests.Support;
using Microsoft.Extensions.Options;

namespace hotjob.Tests;

public sealed class TokenServiceTests
{
    private static TokenService CreateService(JwtOptions? options = null) =>
        new(Options.Create(options ?? new JwtOptions
        {
            SecretKey = "test-secret-key-at-least-32-chars-long",
            Issuer = "HotJobTest",
            Audience = "HotJobTestClient",
            AccessTokenMinutes = 60,
            RefreshTokenDays = 7
        }));

    [Fact]
    public void CreateAccessToken_ContainsExpectedClaims_AndExpiry()
    {
        var service = CreateService();
        var user = new ApplicationUser { Id = "user-1", Email = "u@example.com" };

        var token = service.CreateAccessToken(user, ["Candidate", "Employer"], out var expiresAt);

        Assert.True(expiresAt > DateTime.UtcNow.AddMinutes(59));
        Assert.True(expiresAt <= DateTime.UtcNow.AddMinutes(61));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("HotJobTest", jwt.Issuer);
        Assert.Equal("HotJobTestClient", jwt.Audiences.Single());
        Assert.Equal("user-1", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("user-1", jwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("u@example.com", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(
            ["Candidate", "Employer"],
            jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).OrderBy(v => v).ToArray());
        Assert.NotNull(jwt.Id);
    }

    [Fact]
    public void CreateAccessToken_RespectsConfiguredLifetime()
    {
        var service = CreateService(new JwtOptions
        {
            SecretKey = "test-secret-key-at-least-32-chars-long",
            Issuer = "I",
            Audience = "A",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        });

        service.CreateAccessToken(new ApplicationUser { Id = "u", Email = "u@e.c" }, [], out var expiresAt);

        Assert.True(expiresAt > DateTime.UtcNow.AddMinutes(14));
        Assert.True(expiresAt <= DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public void GenerateRefreshToken_IsUnique_AndUrlSafe()
    {
        var service = CreateService();

        var tokens = Enumerable.Range(0, 100).Select(_ => service.GenerateRefreshToken()).ToArray();

        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t));
            Assert.DoesNotContain("+", t);
            Assert.DoesNotContain("/", t);
            Assert.DoesNotContain("=", t);
        });
    }

    [Fact]
    public void HashToken_IsDeterministic_LowercaseHex64()
    {
        var service = CreateService();

        var first = service.HashToken("abc");
        var second = service.HashToken("abc");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.NotEqual(first, service.HashToken("abd"));
    }

    [Fact]
    public async Task FullPipeline_LoginRefreshLogout_EndToEnd()
    {
        // Guards the wiring between AccountService, AuthService, and the DB in one flow.
        await using var fx = await TestFixture.CreateAsync();
        var register = await fx.Service.RegisterAsync(new DTOs.Authentication.RegisterAccountRequest
        {
            Email = "pipeline@example.com",
            Password = "Str0ng!Pass",
            FullName = "Pipeline User",
            AccountType = DTOs.Authentication.AccountType.Candidate
        });
        Assert.True(register.Success, string.Join("; ", register.Errors));

        var login = await fx.AuthService.LoginAsync(new DTOs.Authentication.LoginRequest
        {
            Email = "pipeline@example.com",
            Password = "Str0ng!Pass"
        });
        Assert.True(login.Success, string.Join("; ", login.Errors));
        Assert.Contains(AppRoles.Candidate, login.Data!.Roles);

        var refresh = await fx.AuthService.RefreshAsync(login.Data.RefreshToken);
        Assert.True(refresh.Success, string.Join("; ", refresh.Errors));

        var logout = await fx.AuthService.LogoutAsync(refresh.Data!.RefreshToken, login.Data.UserId);
        Assert.True(logout.Success);
        Assert.False((await fx.AuthService.RefreshAsync(refresh.Data.RefreshToken)).Success);
    }
}
