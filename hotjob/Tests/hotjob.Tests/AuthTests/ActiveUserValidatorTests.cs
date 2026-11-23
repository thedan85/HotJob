using System.Security.Claims;
using hotjob.Services.Authentication;
using hotjob.Tests.Support;

namespace hotjob.Tests;

public sealed class ActiveUserValidatorTests
{
    private static ClaimsPrincipal Principal(string? userId, string claimType = ClaimTypes.NameIdentifier) =>
        new(new ClaimsIdentity(userId is null ? [] : [new Claim(claimType, userId)], "test"));

    [Fact]
    public async Task ActiveUser_IsAccepted()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("active@example.com");

        Assert.True(await ActiveUserValidator.IsActiveAsync(fx.Db, Principal(user.Id)));
    }

    [Fact]
    public async Task ActiveUser_SubClaim_IsAccepted()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("sub@example.com");

        Assert.True(await ActiveUserValidator.IsActiveAsync(fx.Db, Principal(user.Id, "sub")));
    }

    [Fact]
    public async Task DisabledUser_IsRejected()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("off@example.com");
        user.IsActive = false;
        await fx.Db.SaveChangesAsync();

        Assert.False(await ActiveUserValidator.IsActiveAsync(fx.Db, Principal(user.Id)));
    }

    [Fact]
    public async Task UnknownUser_IsRejected()
    {
        await using var fx = await TestFixture.CreateAsync();

        Assert.False(await ActiveUserValidator.IsActiveAsync(fx.Db, Principal("does-not-exist")));
    }

    [Fact]
    public async Task MissingUserId_IsRejected()
    {
        await using var fx = await TestFixture.CreateAsync();

        Assert.False(await ActiveUserValidator.IsActiveAsync(fx.Db, Principal(null)));
        Assert.False(await ActiveUserValidator.IsActiveAsync(fx.Db, null));
    }
}
