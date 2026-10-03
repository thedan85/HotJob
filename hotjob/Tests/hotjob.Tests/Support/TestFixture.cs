using hotjob.Data;
using hotjob.Models.Users;
using hotjob.Services.Account;
using hotjob.Services.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.Support;

/// <summary>
/// Builds a fully-wired <see cref="AccountService"/> backed by SQLite in-memory
/// plus real Identity managers (same options as Program.cs). One fixture per test.
/// </summary>
public sealed class TestFixture : IAsyncDisposable
{
    private readonly IServiceScope _scope;

    public SqliteConnection Connection { get; }
    public IServiceProvider Services { get; }

    public ApplicationDbContext Db => _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    public UserManager<ApplicationUser> UserManager => _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    public RoleManager<IdentityRole> RoleManager => _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    public AccountService Service => new(
        UserManager,
        RoleManager,
        Db,
        AuthService,
        NullLogger<AccountService>.Instance);

    public AuthService AuthService => new(
        UserManager,
        Db,
        TokenService,
        Options.Create(new JwtOptions
        {
            SecretKey = "test-secret-key-at-least-32-chars-long",
            Issuer = "HotJobTest",
            Audience = "HotJobTestClient",
            AccessTokenMinutes = 60,
            RefreshTokenDays = 7
        }),
        TimeProvider.System,
        NullLogger<AuthService>.Instance);

    public TokenService TokenService => new(Options.Create(new JwtOptions
    {
        SecretKey = "test-secret-key-at-least-32-chars-long",
        Issuer = "HotJobTest",
        Audience = "HotJobTestClient",
        AccessTokenMinutes = 60,
        RefreshTokenDays = 7
    }));

    /// <summary>Hash helper so tests can look up stored token rows without duplicating SHA-256 code.</summary>
    public string AuthServiceHash(string rawToken) => TokenService.HashToken(rawToken);

    /// <summary>Creates a confirmed user via the real register pipeline, for login tests.</summary>
    public async Task<ApplicationUser> SeedUserAsync(
        string email = "user@example.com",
        string password = "Str0ng!Pass",
        string role = AppRoles.Candidate)
    {
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, IsActive = true };
        var result = await UserManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        if (!await RoleManager.RoleExistsAsync(role))
        {
            Assert.True((await RoleManager.CreateAsync(new IdentityRole(role))).Succeeded);
        }

        Assert.True((await UserManager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private TestFixture(SqliteConnection connection, IServiceProvider services, IServiceScope scope)
    {
        Connection = connection;
        Services = services;
        _scope = scope;
    }

    public static async Task<TestFixture> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));

        // Mirror Program.cs Identity options so password/email rules match production.
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 6;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        return new TestFixture(connection, provider, scope);
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await Connection.CloseAsync();
        Connection.Dispose();
        if (Services is IDisposable d) d.Dispose();
        GC.SuppressFinalize(this);
    }
}
