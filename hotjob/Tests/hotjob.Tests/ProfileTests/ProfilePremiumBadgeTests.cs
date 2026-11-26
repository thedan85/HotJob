using hotjob.Components.Pages;
using hotjob.Models.Billing;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace hotjob.Tests;

public sealed partial class ProfilePremiumBadgeTests
{
    [Theory]
    [InlineData(60, true)]   // Active Premium.
    [InlineData(-60, false)] // Expired Premium, with a plan still assigned.
    [InlineData(0, false)]   // Expiry exactly at the current time is not active.
    [InlineData(null, false)] // No Premium subscription.
    public async Task Profile_ShowsPremiumBadgeOnlyForUnexpiredEntitlement(
        int? secondsUntilExpiry, bool expectedBadge)
    {
        await using var fx = await TestFixture.CreateAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "badge@example.test");
        if (secondsUntilExpiry.HasValue)
        {
            var plan = new Plan
            {
                Code = Plan.PremiumMonthlyCode,
                Name = "Premium Monthly",
                Audience = PlanAudience.Candidate,
                Price = 99_000,
                DurationDays = 30
            };
            fx.Db.Plans.Add(plan);
            candidate.PremiumPlan = plan;
            candidate.PremiumUntil = clock.GetUtcNow().AddSeconds(secondsUntilExpiry.Value).UtcDateTime;
            await fx.Db.SaveChangesAsync();
        }

        var session = new AuthSession();
        session.SetTokens(new AuthTokens(
            "test-access", clock.GetUtcNow().AddHours(1).UtcDateTime, "", clock.GetUtcNow().UtcDateTime,
            candidate.UserId, "badge@example.test", ["Candidate"]));
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton(session);
        collection.AddSingleton<HotJobAuthStateProvider>();
        collection.AddSingleton<AuthenticationStateProvider>(
            services => services.GetRequiredService<HotJobAuthStateProvider>());
        collection.AddSingleton<IAccountService>(fx.Service);
        collection.AddSingleton<ICandidatePremiumService>(new CandidatePremiumService(fx.Db, clock));
        collection.AddSingleton(new CircuitDbGate());
        collection.AddSingleton<IResumeService>(new ResumeService(
            fx.Db,
            new CvFileStorage(AppContext.BaseDirectory, NullLogger<CvFileStorage>.Instance),
            clock,
            NullLogger<ResumeService>.Instance));
        collection.AddSingleton<ICandidateSearchService>(new CandidateSearchService(
            fx.Db, new ApplicantRepository(fx.Db), clock));
        collection.AddSingleton<NavigationManager>(new ProfileNavigation());
        collection.AddSingleton<IJSRuntime>(new NoJavaScript());
        await using var services = collection.BuildServiceProvider();
        await using var renderer = new ProfileHtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = renderer.BeginRenderingComponent(typeof(Profile), ParameterView.Empty);
            await root.QuiescenceTask;
            return root.ToHtmlString();
        });

        Assert.Contains("class=\"profile-summary\"", html);
        Assert.Contains("Match Candidate", html);
        Assert.Contains("id=\"change-password\"", html);
        const string badge = "<span class=\"profile-badge profile-badge-green\">Premium</span>";
        if (expectedBadge)
            Assert.Contains(badge, html);
        else
            Assert.DoesNotContain(badge, html);
    }

    // Render the real page's markup without hosting its InteractiveServer circuit.
    private sealed class ProfileHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(
            Type componentType, int? parentComponentId, IComponentActivator componentActivator,
            IComponentRenderMode renderMode) =>
            componentActivator.CreateInstance(componentType);
    }

    private sealed class ProfileNavigation : NavigationManager
    {
        public ProfileNavigation() => Initialize("http://localhost/", "http://localhost/profile");

        protected override void NavigateToCore(string uri, bool forceLoad) =>
            throw new InvalidOperationException("Profile rendering should not navigate.");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Static profile rendering should not invoke JavaScript.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
