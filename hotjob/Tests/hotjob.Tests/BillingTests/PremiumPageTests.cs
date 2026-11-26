using System.Reflection;
using System.Text.RegularExpressions;
using hotjob.Components.Pages;
using hotjob.DTOs.Billing;
using hotjob.Models.Billing;
using hotjob.Services.Authentication;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace hotjob.Tests.BillingTests;

public sealed class PremiumPageTests
{
    [Fact]
    public async Task Render_UsesCatalogueOfferInsteadOfHistoricalSubscription()
    {
        await using var ui = await PremiumPage.CreateAsync(new PremiumStub());
        await ui.SettleAsync();
        var html = await ui.HtmlAsync();

        Assert.Contains("Premium Flex", html);
        Assert.Contains("149,000 VND", html);
        Assert.Contains("45 days", html);
        Assert.DoesNotContain("99.000", html);
        Assert.DoesNotContain("30 days", html);
        Assert.DoesNotContain("Premium Monthly", html);
        Assert.DoesNotContain("Historical subscription", html);
        Assert.Contains("<h2>Premium Flex</h2>", html);
        Assert.DoesNotContain("disabled", PurchaseButton(html));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Render_NoPlan_DisablesSubscribeAndRenew(bool subscribed)
    {
        await using var ui = await PremiumPage.CreateAsync(new PremiumStub
        {
            Plan = null,
            Status = new(subscribed, "Historical subscription", null, null)
        });
        await ui.SettleAsync();
        var html = await ui.HtmlAsync();

        Assert.Contains("Premium is temporarily unavailable. Please try again later.", html);
        Assert.Contains("disabled", PurchaseButton(html));
        AssertNoDefaultOffer(html);
        await ui.BuyAsync();
        Assert.Equal(0, ui.Billing.CheckoutCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Render_LoadingOrFailure_DoesNotOfferPurchase(bool fail)
    {
        var completion = new TaskCompletionSource<Plan?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var ui = await PremiumPage.CreateAsync(new PremiumStub { ReadPlan = () => completion.Task });
        try
        {
            var loading = await ui.HtmlAsync();
            AssertNoEnabledPurchase(loading);
            AssertNoDefaultOffer(loading);
        }
        finally
        {
            if (fail)
                completion.SetException(new InvalidOperationException("Test plan read failed."));
            else
                completion.SetResult(null);
            await ui.SettleAsync();
        }
        var html = await ui.HtmlAsync();
        AssertNoEnabledPurchase(html);
        AssertNoDefaultOffer(html);
        if (fail)
            Assert.Contains("role=\"alert\"", html);
    }

    [Fact]
    public async Task Load_WaitsForCircuitGate()
    {
        var gate = new CircuitDbGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = gate.RunAsync(async () => { entered.SetResult(); await release.Task; });
        await entered.Task;
        var service = new PremiumStub();
        await using var ui = await PremiumPage.CreateAsync(service, gate);
        try
        {
            Assert.Equal(0, service.StatusCalls);
            Assert.Equal(0, service.PlanCalls);
            AssertNoEnabledPurchase(await ui.HtmlAsync());
        }
        finally
        {
            release.SetResult();
            await holding;
            await ui.SettleAsync();
        }
        Assert.Equal(1, service.PlanCalls);
    }

    private static string PurchaseButton(string html) =>
        Assert.Single(Regex.Matches(html, "<button\\b[^>]*>.*?</button>", RegexOptions.Singleline).Cast<Match>()).Value;

    private static void AssertNoEnabledPurchase(string html) =>
        Assert.All(Regex.Matches(html, "<button\\b[^>]*>").Cast<Match>(),
            button => Assert.Contains("disabled", button.Value));

    private static void AssertNoDefaultOffer(string html)
    {
        Assert.DoesNotContain("99.000", html);
        Assert.DoesNotContain("30 days", html);
        Assert.DoesNotContain("Premium Monthly", html);
    }

    private sealed class PremiumStub : ICandidatePremiumService
    {
        public Plan? Plan { get; init; } = new()
        {
            Code = Models.Billing.Plan.PremiumMonthlyCode,
            Audience = PlanAudience.Candidate,
            Name = "Premium Flex",
            Price = 149_000,
            DurationDays = 45
        };
        public CandidatePremiumStatus Status { get; init; } = new(false, "Historical subscription", null, null);
        public Func<Task<Plan?>>? ReadPlan { get; init; }
        public int PlanCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public Task<Plan?> GetPurchasablePlanAsync(CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            return ReadPlan?.Invoke() ?? Task.FromResult(Plan);
        }
        public Task<CandidatePremiumStatus?> GetStatusAsync(string? userId, CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            return Task.FromResult<CandidatePremiumStatus?>(Status);
        }
        public Task<bool> IsPremiumAsync(string? userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Status.IsPremium);
    }

    private sealed class BillingStub : IBillingService
    {
        public int CheckoutCalls { get; private set; }
        public Task<ServiceResult<CheckoutResponse>> CreateCandidatePremiumCheckoutAsync(
            string userId, string planCode, string clientIp, CancellationToken cancellationToken = default)
        {
            CheckoutCalls++;
            return Task.FromResult(ServiceResult<CheckoutResponse>.Fail(ServiceErrorType.Validation, "Test checkout stopped."));
        }
        public Task<ServiceResult<CheckoutResponse>> CreateEmployerPlanCheckoutAsync(
            string userId, string planCode, string clientIp, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ServiceResult<CheckoutResponse>> CreateFeaturedCheckoutAsync(
            string userId, int jobId, string clientIp, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<VnPayProcessResult> ProcessCallbackAsync(
            IReadOnlyDictionary<string, string?> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PremiumPage(
        ServiceProvider services, PageRenderer renderer, HtmlRootComponent root,
        PageActivator activator, BillingStub billing) : IAsyncDisposable
    {
        public BillingStub Billing => billing;

        public static async Task<PremiumPage> CreateAsync(PremiumStub service, CircuitDbGate? gate = null)
        {
            var session = new AuthSession();
            session.SetTokens(new AuthTokens(
                "test-access", DateTime.UtcNow.AddHours(1), "", DateTime.UtcNow,
                "candidate-test", "candidate@example.test", ["Candidate"]));
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddAuthorizationCore();
            collection.AddCascadingAuthenticationState();
            collection.AddSingleton(session);
            collection.AddSingleton<AuthenticationStateProvider, HotJobAuthStateProvider>();
            collection.AddSingleton<ICandidatePremiumService>(service);
            collection.AddSingleton(gate ?? new CircuitDbGate());
            var billing = new BillingStub();
            var activator = new PageActivator();
            collection.AddSingleton<IBillingService>(billing);
            collection.AddSingleton<IComponentActivator>(activator);
            collection.AddSingleton<NavigationManager>(new PageNavigation());
            var services = collection.BuildServiceProvider();
            var renderer = new PageRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(
                () => renderer.BeginRenderingComponent(typeof(Premium), ParameterView.Empty));
            return new(services, renderer, root, activator, billing);
        }

        public Task SettleAsync() => root.QuiescenceTask.WaitAsync(TimeSpan.FromSeconds(10));
        public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        public Task BuyAsync() => renderer.Dispatcher.InvokeAsync(() =>
            (Task)typeof(Premium).GetMethod("BuyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(activator.Page, null)!);

        public async ValueTask DisposeAsync()
        {
            await ((IAsyncDisposable)renderer).DisposeAsync();
            await services.DisposeAsync();
        }
    }

    private sealed class PageActivator : IComponentActivator
    {
        public Premium? Page { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is Premium page) Page = page;
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

    private sealed class PageNavigation : NavigationManager
    {
        public PageNavigation() => Initialize("http://localhost/", "http://localhost/premium");
        protected override void NavigateToCore(string uri, bool forceLoad) =>
            throw new InvalidOperationException("Rendering must not navigate.");
    }
}
