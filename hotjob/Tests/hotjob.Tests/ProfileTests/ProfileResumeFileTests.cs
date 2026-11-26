using System.Reflection;
using hotjob.Components.Pages;
using hotjob.DTOs.Candidate;
using hotjob.Repositories.Employer;
using hotjob.Services.Account;
using hotjob.Services.Authentication;
using hotjob.Services.Billing;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace hotjob.Tests;

public sealed partial class ProfilePremiumBadgeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Profile_EditShowsRemoveFileOnlyForAnExistingAttachment(bool attached)
    {
        await using var page = await ResumePage.CreateAsync(attached);
        await page.EventAsync("EditResumeAsync", page.ResumeId);
        var html = await page.HtmlAsync();
        Assert.Equal(attached, html.Contains(">Remove file</button>", StringComparison.Ordinal));
        Assert.DoesNotContain("Confirm remove file", html);
        if (!attached)
            Assert.Contains("No file attached", html);
    }

    [Fact]
    public async Task Profile_RemoveFileRequiresConfirmationAndCancelKeepsAttachment()
    {
        await using var page = await ResumePage.CreateAsync();
        await page.EventAsync("EditResumeAsync", page.ResumeId);
        await page.EventAsync("RequestRemoveResumeFile");
        var html = await page.HtmlAsync();
        Assert.Contains("Confirm remove file", html);
        Assert.Contains("Past applications keep access", html);
        Assert.NotNull((await page.Fixture.Db.Resumes.FindAsync(page.ResumeId))!.FileUrl);

        await page.EventAsync("CancelRemoveResumeFile");
        Assert.DoesNotContain("Confirm remove file", await page.HtmlAsync());
        Assert.NotNull((await page.Fixture.Db.Resumes.FindAsync(page.ResumeId))!.FileUrl);
    }

    [Fact]
    public async Task Profile_RemoveFileRefreshesViewWithoutDiscardingUnsavedEdits()
    {
        await using var page = await ResumePage.CreateAsync();
        await page.EventAsync("EditResumeAsync", page.ResumeId);
        page.Form.Title = "Unsaved title";
        await page.EventAsync("RequestRemoveResumeFile");
        await page.EventAsync("RemoveResumeFileAsync");

        var html = await page.HtmlAsync();
        Assert.Contains("Attached file removed.", html);
        Assert.Contains("No file attached", html);
        Assert.DoesNotContain(">Remove file</button>", html);
        Assert.DoesNotContain("Confirm remove file", html);
        Assert.Equal("Unsaved title", page.Form.Title);
        var resume = await page.Fixture.Db.Resumes.FindAsync(page.ResumeId);
        Assert.NotNull(resume);
        Assert.Null(resume.FileUrl);
        Assert.Equal("Primary CV", resume.Title);
    }

    [Fact]
    public async Task Profile_RemoveFileDisplaysServiceValidationAndKeepsAttachment()
    {
        await using var page = await ResumePage.CreateAsync();
        page.Fixture.Db.ResumeSkills.RemoveRange(page.Fixture.Db.ResumeSkills);
        await page.Fixture.Db.SaveChangesAsync();
        await page.EventAsync("EditResumeAsync", page.ResumeId);
        await page.EventAsync("RequestRemoveResumeFile");
        await page.EventAsync("RemoveResumeFileAsync");

        var html = await page.HtmlAsync();
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Add and save at least one skill before removing the attached file.", html);
        Assert.Contains(">Remove file</button>", html);
        Assert.NotNull((await page.Fixture.Db.Resumes.FindAsync(page.ResumeId))!.FileUrl);
    }

    [Fact]
    public async Task Profile_RemoveFileWaitsForCircuitGateAndDisablesConfirmationWhileBusy()
    {
        await using var page = await ResumePage.CreateAsync();
        await page.EventAsync("EditResumeAsync", page.ResumeId);
        await page.EventAsync("RequestRemoveResumeFile");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateHeld = page.Gate.RunAsync(() => release.Task);
        Task removing = Task.CompletedTask;
        try
        {
            await page.Renderer.Dispatcher.InvokeAsync(() =>
            {
                removing = page.EventAsync("RemoveResumeFileAsync");
            });
            Assert.False(removing.IsCompleted);
            var html = await page.HtmlAsync();
            Assert.Matches("<button[^>]*disabled[^>]*>Removing\\.\\.\\.</button>", html);
            Assert.NotNull((await page.Fixture.Db.Resumes.FindAsync(page.ResumeId))!.FileUrl);
        }
        finally
        {
            release.SetResult();
            await gateHeld;
            await removing;
        }
        Assert.Contains("Attached file removed.", await page.HtmlAsync());
    }

    // Use the project's existing static renderer, with the real Profile and ResumeService.
    // Events run through ComponentBase's EventCallback handling; private handlers are located by name.
    private sealed class ResumePage(
        TestFixture fixture, ServiceProvider services, ProfileHtmlRenderer renderer,
        CapturingProfileActivator activator, int resumeId) : IAsyncDisposable
    {
        public TestFixture Fixture => fixture;
        public ProfileHtmlRenderer Renderer => renderer;
        public int ResumeId => resumeId;
        public CircuitDbGate Gate => services.GetRequiredService<CircuitDbGate>();
        public ResumeInput Form => (ResumeInput)typeof(Profile)
            .GetField("resumeForm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(activator.Profile)!;
        private Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent root;

        public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);

        public Task EventAsync(string name, params object[] arguments) =>
            renderer.Dispatcher.InvokeAsync(() =>
                EventCallback.Factory.Create(activator.Profile!, async () =>
                {
                    var method = typeof(Profile).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(method);
                    if (method.Invoke(activator.Profile, arguments) is Task task)
                        await task;
                }).InvokeAsync());

        public static async Task<ResumePage> CreateAsync(bool attached = true)
        {
            var fx = await TestFixture.CreateAsync();
            var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "file-ui@example.test", ("C#", 1));
            resume.FileUrl = attached ? "/uploads/cv/1/ui-test.pdf" : null;
            await fx.Db.SaveChangesAsync();
            var clock = TimeProvider.System;
            var session = new AuthSession();
            session.SetTokens(new AuthTokens(
                "test-access", clock.GetUtcNow().AddHours(1).UtcDateTime, "", clock.GetUtcNow().UtcDateTime,
                candidate.UserId, "file-ui@example.test", ["Candidate"]));
            var activator = new CapturingProfileActivator();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton<IComponentActivator>(activator);
            collection.AddSingleton(session);
            collection.AddSingleton<HotJobAuthStateProvider>();
            collection.AddSingleton<AuthenticationStateProvider>(
                services => services.GetRequiredService<HotJobAuthStateProvider>());
            collection.AddSingleton<IAccountService>(fx.Service);
            collection.AddSingleton<ICandidatePremiumService>(new CandidatePremiumService(fx.Db, clock));
            collection.AddSingleton(new CircuitDbGate());
            collection.AddSingleton<IResumeService>(new ResumeService(
                fx.Db, new CvFileStorage(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp", "opencode", $"hotjob-ui-{Guid.NewGuid():N}"), NullLogger<CvFileStorage>.Instance),
                clock, NullLogger<ResumeService>.Instance));
            collection.AddSingleton<ICandidateSearchService>(new CandidateSearchService(
                fx.Db, new ApplicantRepository(fx.Db), clock));
            collection.AddSingleton<NavigationManager>(new ProfileNavigation());
            collection.AddSingleton<IJSRuntime>(new NoJavaScript());
            var provider = collection.BuildServiceProvider();
            var renderer = new ProfileHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var page = new ResumePage(fx, provider, renderer, activator, resume.Id);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                page.root = renderer.BeginRenderingComponent(typeof(Profile), ParameterView.Empty);
                await page.root.QuiescenceTask;
            });
            return page;
        }

        public async ValueTask DisposeAsync()
        {
            await ((IAsyncDisposable)renderer).DisposeAsync();
            await services.DisposeAsync();
            await fixture.DisposeAsync();
        }
    }

    private sealed class CapturingProfileActivator : IComponentActivator
    {
        public Profile? Profile { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is Profile profile)
                Profile = profile;
            return component;
        }
    }
}
