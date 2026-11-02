using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Security.Claims;
using hotjob.Controllers;
using hotjob.Models;
using hotjob.Repositories.Job;
using hotjob.Services.Admin;
using hotjob.Services.Billing;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.AdminTests;

public sealed class SkillApiValidationTests
{
    [Theory]
    [InlineData("POST", "/api/admin/skills", "{\"name\":\"   \"}")]
    [InlineData("POST", "/api/admin/skills", "{")]
    [InlineData("PUT", "/api/admin/skills/1", "{}")]
    [InlineData("GET", "/api/admin/skills?page=invalid", null)]
    [InlineData("GET", "/api/jobs/matching-resume/1?minSkillMatches=invalid", null)]
    public async Task InvalidRequest_ReturnsBadRequestEnvelope(string method, string path, string? json)
    {
        await using var fx = await TestFixture.CreateAsync();
        await using var app = await CreateAppAsync(fx);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.NotEmpty(body.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task OtherController_InvalidRequest_KeepsExistingErrorArray()
    {
        await using var fx = await TestFixture.CreateAsync();
        await using var app = await CreateAppAsync(fx);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await client.GetAsync("/validation-probe?page=invalid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
    }

    [Theory]
    [InlineData("POST", HttpStatusCode.Created)]
    [InlineData("PUT", HttpStatusCode.OK)]
    public async Task PaddedInput_UsesNormalizedLengths(string method, HttpStatusCode expectedStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = new Skill { Name = "Existing" };
        fx.Db.Skills.Add(skill);
        await fx.Db.SaveChangesAsync();
        await using var app = await CreateAppAsync(fx);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var request = new HttpRequestMessage(new HttpMethod(method),
            method == "POST" ? "/api/admin/skills" : $"/api/admin/skills/{skill.Id}")
        {
            Content = JsonContent.Create(new { name = $" {new string('a', 100)} ", category = new string(' ', 101) })
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(new string('a', 100), data.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("category").ValueKind);
    }

    [Theory]
    [InlineData("POST", "name")]
    [InlineData("POST", "category")]
    [InlineData("PUT", "name")]
    [InlineData("PUT", "category")]
    public async Task NormalizedInput_TooLong_ReturnsBadRequestEnvelope(string method, string field)
    {
        await using var fx = await TestFixture.CreateAsync();
        var skill = new Skill { Name = "Existing" };
        fx.Db.Skills.Add(skill);
        await fx.Db.SaveChangesAsync();
        await using var app = await CreateAppAsync(fx);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var request = new HttpRequestMessage(new HttpMethod(method),
            method == "POST" ? "/api/admin/skills" : $"/api/admin/skills/{skill.Id}")
        {
            Content = JsonContent.Create(new
            {
                name = field == "name" ? new string('a', 101) : "Rust",
                category = field == "category" ? new string('a', 101) : null
            })
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.NotEmpty(body.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("Existing", (await fx.Db.Skills.FindAsync(skill.Id))!.Name);
    }

    private static async Task<WebApplication> CreateAppAsync(TestFixture fx)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AdminSkillsController).Assembly)
            .AddApplicationPart(typeof(ValidationProbeController).Assembly)
            .ConfigureApiBehaviorOptions(options =>
            {
                // Mirror the existing Program.cs factory, which unrelated endpoints must retain.
                options.InvalidModelStateResponseFactory = context =>
                    new BadRequestObjectResult(context.ModelState.Values
                        .SelectMany(entry => entry.Errors)
                        .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "The request is invalid."
                            : error.ErrorMessage)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray());
            });
        builder.Services.AddAuthentication("Bearer")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAdminService>(new AdminService(fx.Db, fx.UserManager, fx.RoleManager));
        builder.Services.AddSingleton<IJobService>(new JobService(
            fx.Db,
            new JobPostingRepository(fx.Db),
            new EntitlementService(fx.Db, TimeProvider.System),
            new StubJobAlertService(),
            NullLogger<JobService>.Instance));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(ClaimTypes.Role, "Admin")],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

[ApiController]
[Route("validation-probe")]
public sealed class ValidationProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] int page) => Ok(page);
}
