using hotjob.Models.Admin;
using hotjob.Services.Admin;
using hotjob.Tests.Support;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.AdminTests;

public sealed class AdminAuditServiceTests
{
    private static async Task SeedLogAsync(
        TestFixture fx,
        string action,
        string targetType,
        string targetId,
        string adminEmail,
        DateTime createdAt,
        string? detail = null)
    {
        fx.Db.AdminAuditLogs.Add(new AdminAuditLog
        {
            AdminUserId = "admin-" + adminEmail,
            AdminEmail = adminEmail,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = detail,
            CreatedAt = createdAt
        });
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task LogAsync_WritesAdminInfoFromHttpContext()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.SignInAdmin("admin-1", "admin@example.com");
        var service = fx.CreateAuditService();

        await service.LogAsync(
            AuditActions.CompanyApprove,
            AuditTargetTypes.Company,
            "42",
            "Approved company 'Acme'.");

        var log = await fx.Db.AdminAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("admin-1", log.AdminUserId);
        Assert.Equal("admin@example.com", log.AdminEmail);
        Assert.Equal(AuditActions.CompanyApprove, log.Action);
        Assert.Equal(AuditTargetTypes.Company, log.TargetType);
        Assert.Equal("42", log.TargetId);
        Assert.Equal("Approved company 'Acme'.", log.Detail);
    }

    [Fact]
    public async Task LogAsync_FallsBackToAuthenticationStateForBlazorCircuits()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.SignInAdminOnCircuit("admin-2", "circuit@example.com");
        var service = fx.CreateAuditService();

        await service.LogAsync(AuditActions.JobDelete, AuditTargetTypes.Job, "7", "Deleted job.");

        var log = await fx.Db.AdminAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("admin-2", log.AdminUserId);
        Assert.Equal("circuit@example.com", log.AdminEmail);
    }

    [Fact]
    public async Task LogAsync_AnonymousAdmin_ThrowsAndWritesNothing()
    {
        await using var fx = await TestFixture.CreateAsync();
        var service = fx.CreateAuditService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LogAsync(AuditActions.SkillCreate, AuditTargetTypes.Skill, "1", "Created skill."));

        Assert.Equal(0, await fx.Db.AdminAuditLogs.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LogAsync_MissingIdOrEmail_Throws(bool missingId)
    {
        await using var fx = await TestFixture.CreateAsync();
        var claim = missingId
            ? new Claim(ClaimTypes.Email, "admin@example.com")
            : new Claim(ClaimTypes.NameIdentifier, "admin-1");
        fx.HttpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { claim }, "test"))
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fx.CreateAuditService().LogAsync(AuditActions.UserLock, AuditTargetTypes.User, "1"));
        Assert.Empty(await fx.Db.AdminAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task LogAsync_TrimsDetailAndTruncatesTo500Characters()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.SignInAdmin("admin-1", "admin@example.com");
        var service = fx.CreateAuditService();

        await service.LogAsync(AuditActions.SkillUpdate, AuditTargetTypes.Skill, "1", "  " + new string('x', 600) + "  ");

        var log = await fx.Db.AdminAuditLogs.AsNoTracking().SingleAsync();
        Assert.NotNull(log.Detail);
        Assert.Equal(500, log.Detail!.Length);
        Assert.Equal(new string('x', 500), log.Detail);
    }

    [Fact]
    public async Task GetLogsAsync_FiltersByActionTargetTypeAndAdminEmail()
    {
        await using var fx = await TestFixture.CreateAsync();
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        await SeedLogAsync(fx, AuditActions.CompanyApprove, AuditTargetTypes.Company, "1", "alice@example.com", now);
        await SeedLogAsync(fx, AuditActions.CompanyReject, AuditTargetTypes.Company, "2", "alice@example.com", now.AddMinutes(-1));
        await SeedLogAsync(fx, AuditActions.UserLock, AuditTargetTypes.User, "9", "bob@example.com", now.AddMinutes(-2));
        await SeedLogAsync(fx, AuditActions.SkillCreate, AuditTargetTypes.Skill, "4", "bob@example.com", now.AddMinutes(-3));
        var service = fx.CreateAuditService();

        var byAction = await service.GetLogsAsync(1, 20, action: AuditActions.CompanyReject);
        var byTarget = await service.GetLogsAsync(1, 20, targetType: AuditTargetTypes.Company);
        var byEmail = await service.GetLogsAsync(1, 20, adminEmail: "alice");

        Assert.True(byAction.Success);
        Assert.Single(byAction.Data!.Items);
        Assert.Equal(AuditActions.CompanyReject, byAction.Data.Items[0].Action);
        Assert.Equal(2, byTarget.Data!.TotalCount);
        Assert.Equal(2, byEmail.Data!.TotalCount);
        Assert.All(byEmail.Data.Items, log => Assert.Equal("alice@example.com", log.AdminEmail));
    }

    [Fact]
    public async Task GetLogsAsync_DateRangeIncludesWholeToDay()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedLogAsync(fx, AuditActions.JobDelete, AuditTargetTypes.Job, "1", "admin@example.com",
            new DateTime(2026, 11, 22, 23, 59, 0, DateTimeKind.Utc));
        await SeedLogAsync(fx, AuditActions.JobDelete, AuditTargetTypes.Job, "2", "admin@example.com",
            new DateTime(2026, 11, 23, 18, 30, 0, DateTimeKind.Utc));
        await SeedLogAsync(fx, AuditActions.JobDelete, AuditTargetTypes.Job, "3", "admin@example.com",
            new DateTime(2026, 11, 24, 0, 1, 0, DateTimeKind.Utc));
        var service = fx.CreateAuditService();

        var result = await service.GetLogsAsync(
            1, 20,
            from: new DateTime(2026, 11, 23),
            to: new DateTime(2026, 11, 23));

        Assert.True(result.Success);
        Assert.Single(result.Data!.Items);
        Assert.Equal("2", result.Data.Items[0].TargetId);
    }

    [Fact]
    public async Task GetLogsAsync_PagesNewestFirst()
    {
        await using var fx = await TestFixture.CreateAsync();
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        await SeedLogAsync(fx, AuditActions.UserLock, AuditTargetTypes.User, "1", "admin@example.com", now.AddMinutes(-2));
        await SeedLogAsync(fx, AuditActions.UserUnlock, AuditTargetTypes.User, "1", "admin@example.com", now.AddMinutes(-1));
        await SeedLogAsync(fx, AuditActions.JobDelete, AuditTargetTypes.Job, "5", "admin@example.com", now);
        var service = fx.CreateAuditService();

        var first = await service.GetLogsAsync(1, 2);
        var second = await service.GetLogsAsync(2, 2);

        Assert.True(first.Success);
        Assert.Equal(3, first.Data!.TotalCount);
        Assert.Equal(2, first.Data.TotalPages);
        Assert.Equal(new[] { AuditActions.JobDelete, AuditActions.UserUnlock }, first.Data.Items.Select(log => log.Action));
        Assert.Single(second.Data!.Items);
        Assert.Equal(AuditActions.UserLock, second.Data.Items[0].Action);
    }

    [Fact]
    public async Task GetLogsAsync_ClampsPageAndPageSize()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedLogAsync(fx, AuditActions.SkillCreate, AuditTargetTypes.Skill, "1", "admin@example.com",
            new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc));
        var service = fx.CreateAuditService();

        var result = await service.GetLogsAsync(0, 5000);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(200, result.Data.PageSize);
    }
}
