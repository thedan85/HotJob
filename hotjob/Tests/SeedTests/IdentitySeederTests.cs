using hotjob.Data;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests;

public sealed class IdentitySeederTests
{
    private static SeedOptions ThreeAdmins() => new()
    {
        Admins =
        [
            new SeedAdminAccount { Email = "admin1@example.com", Password = "Admin123!" },
            new SeedAdminAccount { Email = "admin2@example.com", Password = "Admin123!" },
            new SeedAdminAccount { Email = "admin3@example.com", Password = "Admin123!" },
        ]
    };

    [Fact]
    public async Task SeedAsync_WithThreeAdmins_CreatesAllWithAdminRole()
    {
        await using var fx = await TestFixture.CreateAsync();

        await IdentitySeeder.SeedAsync(fx.RoleManager, fx.UserManager, ThreeAdmins(), NullLogger.Instance);

        foreach (var email in new[] { "admin1@example.com", "admin2@example.com", "admin3@example.com" })
        {
            var user = await fx.UserManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True(await fx.UserManager.IsInRoleAsync(user, AppRoles.Admin));
        }
    }

    [Fact]
    public async Task SeedAsync_SecondRun_IsIdempotent()
    {
        await using var fx = await TestFixture.CreateAsync();

        await IdentitySeeder.SeedAsync(fx.RoleManager, fx.UserManager, ThreeAdmins(), NullLogger.Instance);
        await IdentitySeeder.SeedAsync(fx.RoleManager, fx.UserManager, ThreeAdmins(), NullLogger.Instance);

        foreach (var email in new[] { "admin1@example.com", "admin2@example.com", "admin3@example.com" })
        {
            var users = await fx.UserManager.Users.CountAsync(u => u.Email == email);
            Assert.Equal(1, users);
        }
    }

    [Fact]
    public async Task SeedAsync_LegacySingleAdmin_StillSeeds()
    {
        await using var fx = await TestFixture.CreateAsync();
        var seed = new SeedOptions { AdminEmail = "legacy@example.com", AdminPassword = "Admin123!" };

        await IdentitySeeder.SeedAsync(fx.RoleManager, fx.UserManager, seed, NullLogger.Instance);

        var user = await fx.UserManager.FindByEmailAsync("legacy@example.com");
        Assert.NotNull(user);
        Assert.True(await fx.UserManager.IsInRoleAsync(user, AppRoles.Admin));
    }

    [Fact]
    public async Task SeedAsync_EmptyConfig_SeedsRolesButNoUsers()
    {
        await using var fx = await TestFixture.CreateAsync();

        await IdentitySeeder.SeedAsync(fx.RoleManager, fx.UserManager, new SeedOptions(), NullLogger.Instance);

        Assert.True(await fx.RoleManager.RoleExistsAsync(AppRoles.Admin));
        Assert.Equal(0, await fx.UserManager.Users.CountAsync());
    }
}
