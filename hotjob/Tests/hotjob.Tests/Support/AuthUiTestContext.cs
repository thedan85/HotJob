using System.Reflection;
using hotjob.Components;
using hotjob.Components.Pages;
using hotjob.DTOs.Authentication;
using hotjob.Services.Authentication;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Services.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace hotjob.Tests.Support;

/// <summary>Roles already present in the circuit, and the starting browser URL.</summary>
internal sealed class AuthUiOptions
{
    public string[]? Roles { get; set; }
    public string Uri { get; set; } = "http://localhost/";
    public AuthJavaScript? JavaScript { get; set; }
}

/// <summary>
/// StaticHtmlRenderer host that resolves InteractiveServer render modes through a
/// capturing activator, so real auth components can be rendered and their bound
/// handlers invoked without a browser circuit or any new test dependency.
/// </summary>
internal sealed class AuthUiTestContext : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly AuthUiRenderer renderer;
    private readonly CapturingActivator activator;
    private readonly AuthNavigation navigation;
    private readonly AuthSession session;
    private HtmlRootComponent? root;

    private AuthUiTestContext(ServiceProvider services, AuthUiRenderer renderer,
        CapturingActivator activator, AuthNavigation navigation, AuthSession session,
        AuthJavaScript javaScript)
    {
        this.services = services;
        this.renderer = renderer;
        this.activator = activator;
        this.navigation = navigation;
        this.session = session;
        JavaScript = javaScript;
    }

    public AuthNavigation Navigation => navigation;
    public AuthSession Session => session;
    public AuthJavaScript JavaScript { get; }
    public HotJobAuthStateProvider Provider => services.GetRequiredService<HotJobAuthStateProvider>();
    public IReadOnlyList<IComponent> Components => activator.Components;
    public Dispatcher Dispatcher => renderer.Dispatcher;

    public static AuthUiTestContext Create(AuthUiOptions? options = null)
    {
        options ??= new AuthUiOptions();
        var session = new AuthSession();
        if (options.Roles is { Length: > 0 })
        {
            session.SetTokens(new AuthTokens("access", DateTime.UtcNow.AddHours(1), string.Empty,
                DateTime.UtcNow, "user-1", "auth@example.test", options.Roles));
        }

        var javaScript = options.JavaScript ?? new AuthJavaScript();
        var navigation = new AuthNavigation(options.Uri);
        var activator = new CapturingActivator();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddAuthorizationCore();
        collection.AddCascadingAuthenticationState();
        collection.AddSingleton(session);
        collection.AddSingleton<HotJobAuthStateProvider>();
        collection.AddSingleton<AuthenticationStateProvider>(
            s => s.GetRequiredService<HotJobAuthStateProvider>());
        collection.AddSingleton<NavigationManager>(navigation);
        collection.AddSingleton<IJSRuntime>(javaScript);
        collection.AddSingleton<IComponentActivator>(activator);
        collection.AddSingleton(new NotificationFeed(null!, new CircuitDbGate()));
        collection.AddSingleton<INotificationRealtime>(
            new NotificationRealtime(NullLogger<NotificationRealtime>.Instance));
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var services = collection.BuildServiceProvider();
        var renderer = new AuthUiRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return new(services, renderer, activator, navigation, session, javaScript);
    }

    public async Task RenderAsync(Type componentType, ParameterView? parameters = null)
    {
        root = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = renderer.BeginRenderingComponent(componentType, parameters ?? ParameterView.Empty);
            await rendered.QuiescenceTask;
            return rendered;
        });
    }

    public Task<string> HtmlAsync()
    {
        var current = root ?? throw new InvalidOperationException("No component has been rendered.");
        return renderer.Dispatcher.InvokeAsync(current.ToHtmlString);
    }

    /// <summary>Static renderer does not fire after-render callbacks on its own.</summary>
    public Task AfterRenderAsync(IComponent component) =>
        renderer.Dispatcher.InvokeAsync(() => ((IHandleAfterRender)component).OnAfterRenderAsync());

    /// <summary>Invokes a private component handler through a real EventCallback.</summary>
    public Task InvokeAsync(IComponent component, string methodName, params object?[] arguments) =>
        renderer.Dispatcher.InvokeAsync(() => EventCallback.Factory.Create(component, async () =>
        {
            var method = component.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            if (method.Invoke(component, arguments) is Task task) await task;
        }).InvokeAsync());

    public T Single<T>() where T : IComponent => Assert.Single(activator.Components.OfType<T>());

    public T? GetField<T>(IComponent component, string name) => (T?)component.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(component);

    public void SetField(IComponent component, string name, object? value)
    {
        var field = component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(component, value);
    }

    public async ValueTask DisposeAsync()
    {
        await ((IAsyncDisposable)renderer).DisposeAsync();
        await services.DisposeAsync();
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class AuthUiRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(
            Type componentType, int? parentComponentId, IComponentActivator componentActivator,
            IComponentRenderMode renderMode) => componentActivator.CreateInstance(componentType);
    }
}

/// <summary>Records the exact navigation arguments, including forceLoad.</summary>
internal sealed class AuthNavigation : NavigationManager
{
    public AuthNavigation(string uri) => Initialize(uri, uri);

    public List<(string Uri, bool ForceLoad)> Navigations { get; } = [];
    public string? Destination => Navigations.Count > 0 ? Navigations[^1].Uri : null;
    public bool ForceLoad => Navigations.Count > 0 && Navigations[^1].ForceLoad;

    protected override void NavigateToCore(string uri, bool forceLoad) => Navigations.Add((uri, forceLoad));
}

/// <summary>Controllable browser auth helper fake; one handler serves every identifier.</summary>
internal sealed class AuthJavaScript : IJSRuntime
{
    public Func<string, object?[]?, Task<AuthApiResult>> Handler { get; set; } =
        (_, _) => Task.FromResult(new AuthApiResult { Ok = false, Status = 401 });

    public List<string> Calls { get; } = [];
    public int SessionChecks => Calls.Count(id => id == "hotjobAuth.me");
    public int LoginCalls => Calls.Count(id => id == "hotjobAuth.login");
    public int LogoutCalls => Calls.Count(id => id == "hotjobAuth.logout");

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add(identifier);
        try
        {
            var task = Handler(identifier, args);
            return task.IsCompletedSuccessfully
                ? ValueTask.FromResult((TValue)(object)task.Result)
                : new ValueTask<TValue>(Cast<TValue>(task));
        }
        catch (Exception exception)
        {
            return ValueTask.FromException<TValue>(exception);
        }
    }

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);

    private static async Task<TValue> Cast<TValue>(Task<AuthApiResult> task) =>
        (TValue)(object)await task;
}
