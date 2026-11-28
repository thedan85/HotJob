using System.Security.Claims;
using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Users;
using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace hotjob.Tests.Support.ResumeParsing;

internal sealed class ResumeParserTestContext : IAsyncDisposable
{
    public TestFixture Fixture { get; private init; } = null!;
    public string UserId { get; private init; } = "";
    public RecordingHttpHandler Handler { get; } = new();
    public ResumeParserService Service { get; private set; } = null!;
    public static async Task<ResumeParserTestContext> CreateAsync(bool useAi = false, string role = AppRoles.Candidate, bool authenticated = true,
        Action<IServiceCollection>? configure = null)
    {
        var fixture = await TestFixture.CreateAsync(configure);
        var user = await fixture.SeedUserAsync("fake.candidate@example.test", role: role);
        fixture.Db.Candidates.Add(new Candidate { UserId = user.Id, FullName = "Fake Candidate" });
        fixture.Db.Skills.Add(new Skill { Name = "C#" });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var context = new ResumeParserTestContext { Fixture = fixture, UserId = user.Id };
        if (authenticated)
            fixture.AuthStateProvider.SignIn(Principal(user.Id, role));
        var clock = new NoElapsedTimeProvider();
        var parsing = Options.Create(new ResumeParsingOptions { UseAi = useAi });
        var ai = new DeepSeekResumeClient(context.Handler, parsing, Options.Create(new DeepSeekOptions { ApiKey = "test-only-key" }),
            new(clock), new(clock, parsing), clock);
        context.Service = new(fixture.Db, fixture.HttpContextAccessor, fixture.AuthStateProvider,
            new(new(clock), new(), new()), new(), new(), ai);
        return context;
    }
    public static ClaimsPrincipal Principal(string id, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role)], "test"));
    public async ValueTask DisposeAsync() { Handler.Dispose(); await Fixture.DisposeAsync(); }
}
