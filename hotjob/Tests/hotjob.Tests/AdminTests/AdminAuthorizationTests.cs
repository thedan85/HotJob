using System.Reflection;
using System.Security.Claims;
using hotjob.Controllers;
using hotjob.Controllers.Admin;
using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Admin;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace hotjob.Tests.AdminTests;

/// <summary>
/// P0: every admin API is gated server-side to the Admin role over JWT, and the acting
/// admin comes from the token rather than from anything the client sends.
/// </summary>
public sealed class AdminAuthorizationTests
{
    /// <summary>All controllers that expose administrative operations.</summary>
    public static TheoryData<Type> AdminControllers()
    {
        var data = new TheoryData<Type>();
        foreach (var type in AdminControllerTypes())
        {
            data.Add(type);
        }

        return data;
    }

    private static IEnumerable<Type> AdminControllerTypes() =>
        typeof(AdminControllerBase).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(ControllerBase).IsAssignableFrom(type)
                && (typeof(AdminControllerBase).IsAssignableFrom(type)
                    || RouteTemplates(type).Any(IsAdminRoute)))
            .OrderBy(type => type.Name);

    private static IEnumerable<string> RouteTemplates(Type type) =>
        type.GetCustomAttributes<RouteAttribute>(inherit: true).Select(route => route.Template);

    private static bool IsAdminRoute(string template) =>
        template.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase)
        || template.StartsWith("api/dev", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void AdminControllerDiscovery_FindsTheKnownControllers()
    {
        var names = AdminControllerTypes().Select(type => type.Name).ToList();

        Assert.Contains(nameof(CompanyAdminController), names);
        Assert.Contains(nameof(EmployerAdminController), names);
        Assert.Contains(nameof(JobAdminController), names);
        Assert.Contains(nameof(ReviewAdminController), names);
        Assert.Contains(nameof(AdminStatusController), names);
        Assert.Contains(nameof(AdminSkillsController), names);
        Assert.Contains(nameof(DevMatchModelController), names);
    }

    [Theory]
    [MemberData(nameof(AdminControllers))]
    public void AdminController_RequiresAdminRoleOverJwt(Type controller)
    {
        var authorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.Contains(authorize, attribute =>
            attribute.Roles == AppRoles.Admin
            && attribute.AuthenticationSchemes == JwtBearerDefaults.AuthenticationScheme);
    }

    [Theory]
    [MemberData(nameof(AdminControllers))]
    public void AdminController_HasNoAnonymousEscapeHatch(Type controller)
    {
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true));

        var anonymousActions = controller
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(method => method.Name)
            .ToList();

        Assert.Empty(anonymousActions);
    }

    [Theory]
    [MemberData(nameof(AdminControllers))]
    public void AdminController_NoActionAcceptsAnAdminIdFromTheClient(Type controller)
    {
        var offending = controller
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters().Select(parameter => (method, parameter)))
            .Where(pair => ExposesAdminId(pair.parameter))
            .Select(pair => $"{pair.method.Name}({pair.parameter.Name})")
            .ToList();

        Assert.Empty(offending);
    }

    private static bool ExposesAdminId(ParameterInfo parameter)
    {
        if (parameter.Name?.Contains("adminuserid", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return parameter.ParameterType.GetProperties()
            .Any(property => property.Name.Equals("AdminUserId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(AppRoles.Candidate, false)]
    [InlineData(AppRoles.Employer, false)]
    [InlineData(AppRoles.Admin, true)]
    public async Task AdminPolicy_OnlyAdminPrincipalsAreAuthorized(string? role, bool expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        await using var provider = services.BuildServiceProvider();

        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var authorizeData = typeof(JobAdminController).GetCustomAttributes<AuthorizeAttribute>(inherit: true);
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);
        Assert.NotNull(policy);

        var result = await authorizationService.AuthorizeAsync(Principal(role), resource: null, policy!);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task Approve_UsesTheAdminIdFromTheToken()
    {
        var service = new RecordingJobAdminService();
        var controller = WithUser(new JobAdminController(service), Principal(AppRoles.Admin, "admin-from-token"));

        var result = await controller.Approve(7, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal((7, "admin-from-token"), service.Approved);
    }

    [Fact]
    public async Task Reject_UsesTheAdminIdFromTheToken()
    {
        var service = new RecordingJobAdminService();
        var controller = WithUser(new JobAdminController(service), Principal(AppRoles.Admin, "admin-from-token"));

        var result = await controller.Reject(7, new RejectJobRequest("Spam"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal((7, "admin-from-token", "Spam"), service.Rejected);
    }

    [Fact]
    public async Task Approve_WithoutSubjectClaim_Returns401AndDoesNothing()
    {
        var service = new RecordingJobAdminService();
        var roleOnly = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, AppRoles.Admin)], "TestJwt"));
        var controller = WithUser(new JobAdminController(service), roleOnly);

        var result = await controller.Approve(7, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Null(service.Approved);
    }

    [Theory]
    [InlineData(AppRoles.Candidate)]
    [InlineData(AppRoles.Employer)]
    public async Task AuditLog_RejectsNonAdminPrincipal(string role)
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.HttpContextAccessor.HttpContext = new DefaultHttpContext { User = Principal(role, "user-1") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fx.CreateAuditService().LogAsync(AuditActions.UserLock, AuditTargetTypes.User, "1"));
        Assert.Empty(await fx.Db.AdminAuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(AppRoles.Candidate)]
    [InlineData(AppRoles.Employer)]
    public async Task AdminServiceWrite_ByNonAdminOnCircuit_IsRolledBack(string role)
    {
        await using var fx = await TestFixture.CreateAsync();
        var target = await fx.SeedUserAsync("target@example.com");
        Assert.True(target.IsActive);
        fx.AuthStateProvider.SignIn(Principal(role, "intruder-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fx.CreateAdminService().ToggleUserActiveAsync(target.Id));

        var reloaded = await fx.Db.Users.AsNoTracking().SingleAsync(user => user.Id == target.Id);
        Assert.True(reloaded.IsActive);
        Assert.Empty(await fx.Db.AdminAuditLogs.ToListAsync());
    }

    private static ClaimsPrincipal Principal(string? role, string userId = "user-1")
    {
        if (role is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Email, $"{userId}@example.com"),
                new Claim(ClaimTypes.Role, role)
            ],
            "TestJwt"));
    }

    private static T WithUser<T>(T controller, ClaimsPrincipal user) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    private sealed class RecordingJobAdminService : IJobAdminService
    {
        public (int, string)? Approved { get; private set; }
        public (int, string, string)? Rejected { get; private set; }

        public Task<ServiceResult> ApproveAsync(int jobId, string adminUserId, CancellationToken cancellationToken = default)
        {
            Approved = (jobId, adminUserId);
            return Task.FromResult(ServiceResult.Ok());
        }

        public Task<ServiceResult> RejectAsync(int jobId, string adminUserId, string reason, CancellationToken cancellationToken = default)
        {
            Rejected = (jobId, adminUserId, reason);
            return Task.FromResult(ServiceResult.Ok());
        }

        public Task<ServiceResult<PagedResponse<JobListItemDto>>> ListAsync(int page = 1, int pageSize = 20, string? search = null, hotjob.Models.Job.JobStatus? status = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<PagedResponse<JobListItemDto>>> ListPendingAsync(int page = 1, int pageSize = 20, string? search = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<JobDetailDto>> GetDetailAsync(int jobId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
