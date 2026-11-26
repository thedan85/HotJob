using hotjob.Components.Account;
using hotjob.Components;
using hotjob.DTOs.Authentication;
using hotjob.Services.Account.PasswordRecovery;
using hotjob.Services.Authentication;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace hotjob.Tests.PasswordRecoveryTests;

public sealed class RecoveryUiTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("INJECTED_QUERY_NOTICE", false)]
    public async Task Notice_OnlyFixedFlagShowsHardcodedMessage(string flag, bool visible)
    {
        await using var ui = await UiHarness.CreateAsync<PasswordResetNotice>(new() { ["Flag"] = flag });
        var html = await ui.HtmlAsync();
        Assert.Equal(visible, html.Contains("Password reset successfully. Please sign in with your new password."));
        Assert.DoesNotContain("INJECTED_QUERY_NOTICE", html);
        if (visible) Assert.Contains("role=\"status\"", html);
    }

    [Fact]
    public async Task ForgotForm_ValidatesEmailAndShowsOnlyFixedGenericMessage()
    {
        await using var ui = await UiHarness.CreateAsync<ForgotPasswordSection>();
        Assert.False(await ui.ValidateAsync());
        await ui.FillAsync(new() { ["forgot-email"] = "invalid" });
        Assert.False(await ui.ValidateAsync());
        await ui.FillAsync(new() { ["forgot-email"] = "user@example.test" });
        await ui.SubmitAsync();
        var html = await ui.HtmlAsync();
        Assert.Contains(PasswordResetService.RequestNotice, html);
        Assert.Contains("role=\"status\"", html);
        Assert.Contains("href=\"/login\"", html);
        Assert.Equal("user@example.test", ui.Service.RequestedEmail);
        Assert.Equal("https://example.test/", ui.Service.RequestedBaseUrl);
    }

    [Fact]
    public async Task ResetForm_OnlyRequiresMatchingFieldsNotDuplicatedPolicy()
    {
        await using var ui = await ResetUi();
        Assert.False(await ui.ValidateAsync());
        await ui.FillAsync(new() { ["reset-new-password"] = "weak", ["reset-confirm-password"] = "different" });
        Assert.False(await ui.ValidateAsync());
        await ui.FillAsync(new() { ["reset-new-password"] = "weak", ["reset-confirm-password"] = "weak" });
        Assert.True(await ui.ValidateAsync());
        ui.Service.ResetResult = ServiceResult.Fail(ServiceErrorType.Validation, "Identity policy description");
        await ui.SubmitAsync();
        Assert.Contains("Identity policy description", await ui.HtmlAsync());
        Assert.Null(ui.Navigation.Destination);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetSuccess_ClearsPasswordsSignsOutAndRedirectsEvenIfCleanupFails(bool cleanupFails)
    {
        await using var ui = await ResetUi();
        ui.JavaScript.Fails = cleanupFails;
        ui.JavaScript.ReadHtml = () => ui.Root.ToHtmlString();
        await ui.FillAsync(new() { ["reset-new-password"] = "New123!Pass", ["reset-confirm-password"] = "New123!Pass" });
        await ui.SubmitAsync();
        Assert.Equal("/login?passwordReset=1", ui.Navigation.Destination);
        Assert.True(ui.Navigation.ForceLoad);
        Assert.False(ui.Session.IsAuthenticated);
        Assert.DoesNotContain("New123!Pass", ui.JavaScript.HtmlAtLogout);
        var form = Assert.IsType<ResetPasswordForm>(ui.Activator.Components.OfType<EditForm>().Last().Model);
        Assert.Empty(form.NewPassword);
        Assert.Empty(form.ConfirmPassword);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "token")]
    [InlineData("user@example.test", "")]
    public async Task MissingQuery_ShowsGenericInvalidLinkAndRetry(string? email, string? token)
    {
        await using var ui = await UiHarness.CreateAsync<ResetPasswordSection>(
            new() { ["Email"] = email, ["Token"] = token });
        Assert.Contains(PasswordResetService.InvalidLink, await ui.HtmlAsync());
        Assert.Contains("href=\"/forgot-password\"", await ui.HtmlAsync());
        Assert.DoesNotContain("<form", await ui.HtmlAsync());
    }

    [Fact]
    public async Task InvalidToken_ShowsOneFixedErrorAndRetry()
    {
        await using var ui = await ResetUi();
        ui.Service.ResetResult = ServiceResult.Fail(ServiceErrorType.Validation, PasswordResetService.InvalidLink);
        await ui.FillAsync(new() { ["reset-new-password"] = "New123!Pass", ["reset-confirm-password"] = "New123!Pass" });
        await ui.SubmitAsync();
        var html = await ui.HtmlAsync();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, PasswordResetService.InvalidLink));
        Assert.Contains("href=\"/forgot-password\"", html);
        Assert.Null(ui.Navigation.Destination);
    }

    [Fact]
    public async Task QueryChange_DiscardsPreviousPasswordAndErrors()
    {
        await using var ui = await ResetUi();
        ui.Service.ResetResult = ServiceResult.Fail(ServiceErrorType.Validation, "previous-policy-error");
        await ui.FillAsync(new() { ["reset-new-password"] = "New123!Pass", ["reset-confirm-password"] = "New123!Pass" });
        await ui.SubmitAsync();
        await ui.Renderer.Dispatcher.InvokeAsync(() => ui.Activator.Components.OfType<ResetPasswordSection>().Single()
            .SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Email"] = "next@example.test", ["Token"] = "next-token"
            })));
        var html = await ui.HtmlAsync();
        Assert.DoesNotContain("New123!Pass", html);
        Assert.DoesNotContain("previous-policy-error", html);
    }

    [Fact]
    public async Task Reset_WaitsForCircuitGateDisablesControlsAndIgnoresDuplicateSubmit()
    {
        await using var ui = await ResetUi();
        await ui.FillAsync(new() { ["reset-new-password"] = "New123!Pass", ["reset-confirm-password"] = "New123!Pass" });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = ui.Gate.RunAsync(async () => { entered.SetResult(); await release.Task; });
        await entered.Task;
        var submission = ui.SubmitAsync();
        try
        {
            var html = await ui.HtmlAsync();
            Assert.Contains("aria-busy=\"true\"", html);
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, " disabled").Count);
            await ui.SubmitAsync();
            Assert.Equal(0, ui.Service.ResetCalls);
        }
        finally
        {
            release.SetResult();
            await holding;
            await submission;
        }
        Assert.Equal(1, ui.Service.ResetCalls);
    }

    private static Task<UiHarness> ResetUi() => UiHarness.CreateAsync<ResetPasswordSection>(
        new() { ["Email"] = "user@example.test", ["Token"] = "encoded-test-token" });
}

internal sealed class StubRecoveryService : IPasswordResetService
{
    public ServiceResult ResetResult { get; set; } = ServiceResult.Ok();
    public string? RequestedEmail { get; private set; }
    public string? RequestedBaseUrl { get; private set; }
    public int ResetCalls { get; private set; }
    public Task<ServiceResult<string>> RequestPasswordResetAsync(string? email, string baseUrl, CancellationToken cancellationToken = default)
    {
        RequestedEmail = email;
        RequestedBaseUrl = baseUrl;
        // UI must use its own hardcoded generic copy, not arbitrary returned text.
        return Task.FromResult(ServiceResult<string>.Ok("INJECTED_SERVICE_NOTICE"));
    }
    public Task<ServiceResult> ResetPasswordAsync(string? email, string? token, string newPassword, CancellationToken cancellationToken = default)
    {
        ResetCalls++;
        return Task.FromResult(ResetResult);
    }
}

internal sealed class UiHarness(
    ServiceProvider services, HtmlRenderer renderer, HtmlRootComponent root,
    RecoveryActivator activator, StubRecoveryService service, RecoveryNavigation navigation,
    RecoveryJavaScript js, AuthSession session, CircuitDbGate gate) : IAsyncDisposable
{
    public HtmlRenderer Renderer => renderer;
    public HtmlRootComponent Root => root;
    public RecoveryActivator Activator => activator;
    public StubRecoveryService Service => service;
    public RecoveryNavigation Navigation => navigation;
    public RecoveryJavaScript JavaScript => js;
    public AuthSession Session => session;
    public CircuitDbGate Gate => gate;

    public static async Task<UiHarness> CreateAsync<T>(Dictionary<string, object?>? parameters = null) where T : IComponent
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        var service = new StubRecoveryService();
        var navigation = new RecoveryNavigation();
        var js = new RecoveryJavaScript();
        var session = new AuthSession();
        session.SetTokens(new AuthTokens("dummy-access", DateTime.UtcNow.AddMinutes(1), "", DateTime.UtcNow,
            "dummy-user", "user@example.test", ["Candidate"]));
        var gate = new CircuitDbGate();
        var activator = new RecoveryActivator();
        collection.AddSingleton<IPasswordResetService>(service);
        collection.AddSingleton<NavigationManager>(navigation);
        collection.AddSingleton<IJSRuntime>(js);
        collection.AddSingleton(session);
        collection.AddSingleton<HotJobAuthStateProvider>();
        collection.AddSingleton(gate);
        collection.AddSingleton<IComponentActivator>(activator);
        var services = collection.BuildServiceProvider();
        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<T>(
            ParameterView.FromDictionary(parameters ?? [])));
        return new UiHarness(services, renderer, root, activator, service, navigation, js, session, gate);
    }

    public Task FillAsync(Dictionary<string, string> values) => renderer.Dispatcher.InvokeAsync(async () =>
    {
        foreach (var input in activator.Components.OfType<InputText>().TakeLast(values.Count))
        {
            var id = input.AdditionalAttributes!["id"]!.ToString()!;
            if (values.TryGetValue(id, out var value)) await input.ValueChanged.InvokeAsync(value);
        }
    });
    public Task<bool> ValidateAsync() => renderer.Dispatcher.InvokeAsync(() =>
        activator.Components.OfType<EditForm>().Last().EditContext!.Validate());
    public Task SubmitAsync() => renderer.Dispatcher.InvokeAsync(async () =>
    {
        var form = activator.Components.OfType<EditForm>().Last();
        Assert.True(form.EditContext!.Validate());
        await form.OnValidSubmit.InvokeAsync(form.EditContext);
    });
    public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
    public async ValueTask DisposeAsync()
    {
        await renderer.DisposeAsync();
        await services.DisposeAsync();
    }
}

internal sealed class RecoveryActivator : IComponentActivator
{
    public List<IComponent> Components { get; } = [];
    public IComponent CreateInstance(Type type)
    {
        var component = (IComponent)Activator.CreateInstance(type)!;
        Components.Add(component);
        return component;
    }
}

internal sealed class RecoveryNavigation : NavigationManager
{
    public string? Destination { get; private set; }
    public bool ForceLoad { get; private set; }
    public RecoveryNavigation() => Initialize("https://example.test/", "https://example.test/reset-password");
    protected override void NavigateToCore(string uri, bool forceLoad) { Destination = uri; ForceLoad = forceLoad; }
}

internal sealed class RecoveryJavaScript : IJSRuntime
{
    public bool Fails { get; set; }
    public Func<string>? ReadHtml { get; set; }
    public string HtmlAtLogout { get; private set; } = "";
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
    {
        Assert.Equal("hotjobAuth.logout", identifier);
        HtmlAtLogout = ReadHtml?.Invoke() ?? "";
        if (Fails) throw new JSException("SENTINEL_EXCEPTION");
        return ValueTask.FromResult((T)(object)new AuthApiResult { Ok = true, Status = 204 });
    }
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<T>(identifier, args);
}
