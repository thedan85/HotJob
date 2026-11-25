using hotjob.Components;
using hotjob.Components.Account;
using hotjob.Services.Account;
using hotjob.Services.Authentication;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace hotjob.Tests;

public sealed class ChangePasswordSectionTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("unauthorized")]
    [InlineData("error")]
    [InlineData("exception")]
    [InlineData("timeout")]
    public async Task Submit_Success_ClearsFieldsAndSignsOutEvenIfCookieCleanupFails(string cleanup)
    {
        await using var fx = await TestFixture.CreateAsync();
        await using var ui = await FormHarness.CreateAsync(fx, cleanup);
        await ui.FillAsync("Str0ng!Pass", "New123!Pass");

        await ui.SubmitAsync();

        Assert.False(ui.Session.IsAuthenticated);
        Assert.Equal("/login?passwordChanged=1", ui.Navigation.Destination);
        Assert.True(ui.Navigation.ForceLoad);
        Assert.Equal(1, ui.JavaScript.LogoutCalls);
        Assert.DoesNotContain("Str0ng!Pass", ui.JavaScript.HtmlAtLogout);
        Assert.DoesNotContain("New123!Pass", ui.JavaScript.HtmlAtLogout);
        Assert.All(ui.Activator.Components.OfType<EditForm>().TakeLast(1), editForm =>
        {
            var form = Assert.IsType<ChangePasswordForm>(editForm.Model);
            Assert.Empty(form.CurrentPassword);
            Assert.Empty(form.NewPassword);
            Assert.Empty(form.ConfirmNewPassword);
        });
    }

    [Theory]
    [InlineData("Wrong123!", "New123!Pass", "Current password is incorrect.")]
    [InlineData("Str0ng!Pass", "weak", "Password must contain an uppercase letter")]
    public async Task Submit_ServiceFailure_ShowsErrorsAndKeepsSession(
        string currentPassword, string newPassword, string expectedError)
    {
        await using var fx = await TestFixture.CreateAsync();
        await using var ui = await FormHarness.CreateAsync(fx);
        await ui.FillAsync(currentPassword, newPassword);

        await ui.SubmitAsync();

        var html = await ui.HtmlAsync();
        Assert.Contains(expectedError, html);
        Assert.Contains("role=\"alert\"", html);
        Assert.True(ui.Session.IsAuthenticated);
        Assert.Null(ui.Navigation.Destination);
        Assert.Equal(0, ui.JavaScript.LogoutCalls);
    }

    [Fact]
    public async Task Submit_SessionHasEnded_DoesNotChangePassword()
    {
        await using var fx = await TestFixture.CreateAsync();
        await using var ui = await FormHarness.CreateAsync(fx);
        await ui.FillAsync("Str0ng!Pass", "New123!Pass");
        await ui.SignOutAsync();

        await ui.SubmitAsync();

        Assert.Contains("Please sign in to change your password.", await ui.HtmlAsync());
        var user = await fx.UserManager.FindByEmailAsync("user@example.com");
        Assert.True(await fx.UserManager.CheckPasswordAsync(user!, "Str0ng!Pass"));
        Assert.Null(ui.Navigation.Destination);
        Assert.Equal(0, ui.JavaScript.LogoutCalls);
    }

    [Fact]
    public async Task Submit_WaitingForCircuitGate_DisablesControlsAndIgnoresDuplicateSubmission()
    {
        await using var fx = await TestFixture.CreateAsync();
        await using var ui = await FormHarness.CreateAsync(fx);
        await ui.FillAsync("Str0ng!Pass", "New123!Pass");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdingGate = ui.Gate.RunAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;

        var submission = ui.SubmitAsync();
        try
        {
            var html = await ui.HtmlAsync();
            Assert.Contains("Changing password...", html);
            Assert.Contains("aria-busy=\"true\"", html);
            Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, " disabled").Count);
            await ui.SubmitAsync();
            Assert.False(submission.IsCompleted);
        }
        finally
        {
            release.SetResult();
            await holdingGate;
            await submission;
        }

        Assert.Equal(1, ui.JavaScript.LogoutCalls);
    }

    private sealed class FormHarness(
        ServiceProvider services, HtmlRenderer renderer, HtmlRootComponent root,
        CapturingActivator activator, AuthSession session, TestNavigationManager navigation,
        LogoutJavaScript javaScript, CircuitDbGate gate) : IAsyncDisposable
    {
        public CapturingActivator Activator => activator;
        public AuthSession Session => session;
        public TestNavigationManager Navigation => navigation;
        public LogoutJavaScript JavaScript => javaScript;
        public CircuitDbGate Gate => gate;

        public static async Task<FormHarness> CreateAsync(TestFixture fx, string cleanup = "success")
        {
            var user = await fx.SeedUserAsync();
            var session = new AuthSession();
            session.SetTokens(new AuthTokens(
                "access", DateTime.UtcNow.AddMinutes(60), "", DateTime.UtcNow,
                user.Id, user.Email!, ["Candidate"]));
            var navigation = new TestNavigationManager();
            var javaScript = new LogoutJavaScript(cleanup);
            var gate = new CircuitDbGate();
            var activator = new CapturingActivator();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton<IAccountService>(fx.Service);
            collection.AddSingleton(session);
            collection.AddSingleton<HotJobAuthStateProvider>();
            collection.AddSingleton(gate);
            collection.AddSingleton<IComponentActivator>(activator);
            collection.AddSingleton<NavigationManager>(navigation);
            collection.AddSingleton<IJSRuntime>(javaScript);
            var services = collection.BuildServiceProvider();
            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(
                () => renderer.RenderComponentAsync<ChangePasswordSection>());
            javaScript.ReadHtml = root.ToHtmlString;
            return new FormHarness(services, renderer, root, activator, session, navigation, javaScript, gate);
        }

        public Task FillAsync(string currentPassword, string newPassword) =>
            renderer.Dispatcher.InvokeAsync(async () =>
            {
                foreach (var input in activator.Components.OfType<InputText>())
                {
                    var id = input.AdditionalAttributes!["id"]?.ToString();
                    await input.ValueChanged.InvokeAsync(id == "current-password" ? currentPassword : newPassword);
                }
            });

        public Task SubmitAsync() => renderer.Dispatcher.InvokeAsync(async () =>
        {
            var editForm = activator.Components.OfType<EditForm>().Last();
            Assert.True(editForm.EditContext!.Validate());
            await editForm.OnValidSubmit.InvokeAsync(editForm.EditContext);
        });

        public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);

        public Task SignOutAsync() => renderer.Dispatcher.InvokeAsync(
            services.GetRequiredService<HotJobAuthStateProvider>().SignOut);

        public async ValueTask DisposeAsync()
        {
            await renderer.DisposeAsync();
            await services.DisposeAsync();
        }
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)System.Activator.CreateInstance(componentType)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public string? Destination { get; private set; }
        public bool ForceLoad { get; private set; }

        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/profile");

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Destination = uri;
            ForceLoad = forceLoad;
        }
    }

    private sealed class LogoutJavaScript(string cleanup) : IJSRuntime
    {
        public int LogoutCalls { get; private set; }
        public Func<string>? ReadHtml { get; set; }
        public string HtmlAtLogout { get; private set; } = string.Empty;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("hotjobAuth.logout", identifier);
            LogoutCalls++;
            HtmlAtLogout = ReadHtml!();
            if (cleanup == "exception")
                throw new JSException("Cookie cleanup unavailable.");
            if (cleanup == "timeout")
                throw new TaskCanceledException("Cookie cleanup timed out.");

            var result = new AuthApiResult
            {
                Ok = cleanup == "success",
                Status = cleanup == "success" ? 204 : cleanup == "unauthorized" ? 401 : 500
            };
            return ValueTask.FromResult((TValue)(object)result);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
