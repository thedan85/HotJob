using hotjob.Components;
using hotjob.Components.Pages;
using hotjob.DTOs.Authentication;
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
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace hotjob.Tests;

public sealed class ProfileAuthorizationTests
{
    [Fact]
    public async Task AnonymousRender_HidesProfileAndRedirectsToLogin()
    {
        await using var ui = await ProfilePage.CreateAsync();
        var html = await ui.HtmlAsync();

        Assert.Contains("Checking your sign-in...", html);
        Assert.DoesNotContain("class=\"profile-summary\"", html);
        Assert.DoesNotContain("<form", html);
        await ui.RunRedirectAsync();

        Assert.Equal("/login", ui.Navigation.Destination);
        Assert.False(ui.Session.IsAuthenticated);
        Assert.Equal(1, ui.JavaScript.SessionChecks);
    }

    [Fact]
    public async Task BrowserSession_RestoresCandidateWithoutRedirect()
    {
        await using var ui = await ProfilePage.CreateAsync(restoreCandidate: true);

        await ui.RunRedirectAsync();

        Assert.True(ui.Session.IsAuthenticated);
        AssertCandidateProfile(await ui.HtmlAsync());
        Assert.Null(ui.Navigation.Destination);
        Assert.Equal(1, ui.JavaScript.SessionChecks);
    }

    [Fact]
    public async Task CandidateRender_PreservesProfileAndPasswordSection()
    {
        await using var ui = await ProfilePage.CreateAsync(role: "Candidate");

        AssertCandidateProfile(await ui.HtmlAsync());
        Assert.Empty(ui.Activator.Components.OfType<RedirectToLogin>());
        Assert.Null(ui.Navigation.Destination);
    }

    [Fact]
    public async Task EmployerRender_DeniesProfileWithoutLoginRedirect()
    {
        await using var ui = await ProfilePage.CreateAsync(role: "Employer");
        Assert.DoesNotContain("class=\"profile-summary\"", await ui.HtmlAsync());

        await ui.RunRedirectAsync();

        Assert.Contains("You do not have access to this page.", await ui.HtmlAsync());
        Assert.Null(ui.Navigation.Destination);
        Assert.Equal(0, ui.JavaScript.SessionChecks);
    }

    [Fact]
    public async Task SignOut_RemovesProfileMarkup()
    {
        await using var ui = await ProfilePage.CreateAsync(role: "Candidate");
        AssertCandidateProfile(await ui.HtmlAsync());

        await ui.Renderer.Dispatcher.InvokeAsync(ui.Provider.SignOut);
        var html = await ui.HtmlAsync();

        Assert.DoesNotContain("class=\"profile-summary\"", html);
        Assert.DoesNotContain("<form", html);
        Assert.Contains("Checking your sign-in...", html);
    }

    private static void AssertCandidateProfile(string html)
    {
        Assert.Contains("class=\"profile-summary\"", html);
        Assert.Contains("Match Candidate", html);
        Assert.Contains("id=\"change-password\"", html);
    }

    private sealed class ProfilePage(
        TestFixture fixture, ServiceProvider services, PageRenderer renderer,
        HtmlRootComponent root, ProfileActivator activator, ProfileNavigation navigation,
        SessionJavaScript javaScript, AuthSession session) : IAsyncDisposable
    {
        public PageRenderer Renderer => renderer;
        public ProfileActivator Activator => activator;
        public ProfileNavigation Navigation => navigation;
        public SessionJavaScript JavaScript => javaScript;
        public AuthSession Session => session;
        public HotJobAuthStateProvider Provider => services.GetRequiredService<HotJobAuthStateProvider>();

        public static async Task<ProfilePage> CreateAsync(string? role = null, bool restoreCandidate = false)
        {
            var fx = await TestFixture.CreateAsync();
            var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "guard@example.test");
            var session = new AuthSession();
            if (role is not null)
                session.SetTokens(new AuthTokens("test-access", DateTime.UtcNow.AddHours(1), "", DateTime.UtcNow,
                    candidate.UserId, "guard@example.test", [role]));
            var js = new SessionJavaScript
            {
                Result = restoreCandidate
                    ? new AuthApiResult
                    {
                        Ok = true,
                        Status = 200,
                        Data = new AuthResponse("test-access", DateTime.UtcNow.AddHours(1),
                            candidate.UserId, "guard@example.test", ["Candidate"])
                    }
                    : new AuthApiResult { Ok = false, Status = 401 }
            };
            var navigation = new ProfileNavigation();
            var activator = new ProfileActivator();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddAuthorizationCore();
            collection.AddCascadingAuthenticationState();
            collection.AddSingleton(session);
            collection.AddSingleton<HotJobAuthStateProvider>();
            collection.AddSingleton<AuthenticationStateProvider>(
                services => services.GetRequiredService<HotJobAuthStateProvider>());
            collection.AddSingleton<IAccountService>(fx.Service);
            collection.AddSingleton<ICandidatePremiumService>(new CandidatePremiumService(fx.Db, TimeProvider.System));
            collection.AddSingleton(new CircuitDbGate());
            collection.AddSingleton<IResumeService>(new ResumeService(fx.Db,
                new CvFileStorage(AppContext.BaseDirectory, NullLogger<CvFileStorage>.Instance),
                TimeProvider.System, NullLogger<ResumeService>.Instance));
            collection.AddSingleton<ICandidateSearchService>(new CandidateSearchService(
                fx.Db, new ApplicantRepository(fx.Db), TimeProvider.System));
            collection.AddSingleton<NavigationManager>(navigation);
            collection.AddSingleton<IJSRuntime>(js);
            collection.AddSingleton<IComponentActivator>(activator);
            var services = collection.BuildServiceProvider();
            var renderer = new PageRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = renderer.BeginRenderingComponent(typeof(Profile), ParameterView.Empty);
                await root.QuiescenceTask;
                return root;
            });
            return new(fx, services, renderer, root, activator, navigation, js, session);
        }

        public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        public Task RunRedirectAsync() => renderer.Dispatcher.InvokeAsync(() =>
            ((IHandleAfterRender)Assert.Single(activator.Components.OfType<RedirectToLogin>())).OnAfterRenderAsync());

        public async ValueTask DisposeAsync()
        {
            await ((IAsyncDisposable)renderer).DisposeAsync();
            await services.DisposeAsync();
            await fixture.DisposeAsync();
        }
    }

    private sealed class ProfileActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class PageRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(
            Type componentType, int? parentComponentId, IComponentActivator componentActivator,
            IComponentRenderMode renderMode) => componentActivator.CreateInstance(componentType);
    }

    private sealed class ProfileNavigation : NavigationManager
    {
        public string? Destination { get; private set; }
        public ProfileNavigation() => Initialize("http://localhost/", "http://localhost/profile");
        protected override void NavigateToCore(string uri, bool forceLoad) => Destination = uri;
    }

    private sealed class SessionJavaScript : IJSRuntime
    {
        public required AuthApiResult Result { get; init; }
        public int SessionChecks { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("hotjobAuth.me", identifier);
            SessionChecks++;
            return ValueTask.FromResult((TValue)(object)Result);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
