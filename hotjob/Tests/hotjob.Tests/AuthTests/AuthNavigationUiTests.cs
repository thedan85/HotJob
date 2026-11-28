using hotjob.Components;
using hotjob.Components.Layout;
using hotjob.Components.Pages;
using hotjob.DTOs.Authentication;
using hotjob.Services.Authentication;
using hotjob.Tests.Support;
using Microsoft.JSInterop;

namespace hotjob.Tests;

public sealed class AuthNavigationUiTests
{
    private static AuthApiResult Success(string[] roles) => new()
    {
        Ok = true,
        Status = 200,
        Data = new AuthResponse("access", DateTime.UtcNow.AddHours(1),
            "user-1", "auth@example.test", roles)
    };

    private static async Task<AuthUiTestContext> RenderLoginAsync(
        Func<string, object?[]?, Task<AuthApiResult>> handler)
    {
        var ui = AuthUiTestContext.Create(new AuthUiOptions
        {
            JavaScript = new AuthJavaScript { Handler = handler }
        });
        await ui.RenderAsync(typeof(LoginPage));
        var page = ui.Single<LoginPage>();
        var form = ui.GetField<LoginRequest>(page, "form");
        Assert.NotNull(form);
        form.Email = "auth@example.test";
        form.Password = "Str0ng!Pass";
        return ui;
    }

    private static async Task<(AuthUiTestContext Ui, NavMenu Component)> RenderNavAsync(
        Func<string, object?[]?, Task<AuthApiResult>> handler)
    {
        var ui = AuthUiTestContext.Create(new AuthUiOptions
        {
            Roles = ["Employer"],
            Uri = "http://localhost/login",
            JavaScript = new AuthJavaScript { Handler = handler }
        });
        await ui.RenderAsync(typeof(NavMenu));
        return (ui, ui.Single<NavMenu>());
    }

    [Fact]
    public async Task LoginSuccess_ForcesDocumentNavigation()
    {
        var ui = await RenderLoginAsync((id, _) => Task.FromResult(
            id == "hotjobAuth.login" ? Success(["Candidate"]) : new AuthApiResult { Ok = false, Status = 401 }));

        await ui.InvokeAsync(ui.Single<LoginPage>(), "SubmitAsync");

        Assert.Equal("/", ui.Navigation.Destination);
        Assert.True(ui.Navigation.ForceLoad);
        Assert.Equal(1, ui.JavaScript.LoginCalls);
        Assert.True(ui.Session.IsAuthenticated);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task FailedLogin_DoesNotNavigate()
    {
        var ui = await RenderLoginAsync((_, _) => Task.FromResult(new AuthApiResult
        {
            Ok = false,
            Status = 401,
            Errors = ["Invalid email or password"]
        }));

        await ui.InvokeAsync(ui.Single<LoginPage>(), "SubmitAsync");

        Assert.Null(ui.Navigation.Destination);
        Assert.False(ui.Session.IsAuthenticated);
        Assert.Contains("Invalid email or password", await ui.HtmlAsync());
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task LogoutSuccess_ClearsProviderAndForcesDocumentNavigation()
    {
        var (ui, component) = await RenderNavAsync((_, _) => Task.FromResult(
            new AuthApiResult { Ok = true, Status = 204 }));

        await ui.InvokeAsync(component, "SignOutAsync");

        Assert.False(ui.Session.IsAuthenticated);
        Assert.Equal("/", ui.Navigation.Destination);
        Assert.True(ui.Navigation.ForceLoad);
        Assert.Equal(1, ui.JavaScript.LogoutCalls);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task Logout401_ClearsProviderAndForcesDocumentNavigation()
    {
        var (ui, component) = await RenderNavAsync((_, _) => Task.FromResult(
            new AuthApiResult { Ok = false, Status = 401 }));

        await ui.InvokeAsync(component, "SignOutAsync");

        Assert.False(ui.Session.IsAuthenticated);
        Assert.Equal("/", ui.Navigation.Destination);
        Assert.True(ui.Navigation.ForceLoad);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task Logout500_PreservesSessionAndShowsRetry()
    {
        var (ui, component) = await RenderNavAsync((_, _) => Task.FromResult(
            new AuthApiResult { Ok = false, Status = 500 }));

        await ui.InvokeAsync(component, "SignOutAsync");

        Assert.True(ui.Session.IsAuthenticated);
        Assert.Null(ui.Navigation.Destination);
        var html = await ui.HtmlAsync();
        Assert.Contains("We could not sign you out. Please try again.", html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Try again", html);
        Assert.DoesNotContain("Signing out...", html);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task LogoutJavaScriptException_PreservesSessionAndShowsRetry()
    {
        var (ui, component) = await RenderNavAsync(
            (_, _) => Task.FromException<AuthApiResult>(new JSException("bridge unavailable")));

        await ui.InvokeAsync(component, "SignOutAsync");

        Assert.True(ui.Session.IsAuthenticated);
        Assert.Null(ui.Navigation.Destination);
        var html = await ui.HtmlAsync();
        Assert.Contains("We could not sign you out. Please try again.", html);
        Assert.Contains("Try again", html);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task MenuToggle_ReportsExpandedStateToAssistiveTechnology()
    {
        var (ui, component) = await RenderNavAsync((_, _) => Task.FromResult(
            new AuthApiResult { Ok = false, Status = 401 }));
        await using var context = ui;

        Assert.Contains("aria-expanded=\"false\"", await ui.HtmlAsync());

        await ui.InvokeAsync(component, "ToggleMenu");
        Assert.Contains("aria-expanded=\"true\"", await ui.HtmlAsync());

        await ui.InvokeAsync(component, "ToggleMenu");
        Assert.Contains("aria-expanded=\"false\"", await ui.HtmlAsync());
    }

    [Fact]
    public async Task PendingLogout_IgnoresDuplicateClicks()
    {
        var pending = new TaskCompletionSource<AuthApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (ui, component) = await RenderNavAsync((_, _) => pending.Task);

        // Queue the duplicate clicks without awaiting them: they cannot complete
        // until the browser promise below is released. HtmlAsync is queued last,
        // so observing it proves all three handlers have started.
        var clicks = new[]
        {
            ui.InvokeAsync(component, "SignOutAsync"),
            ui.InvokeAsync(component, "SignOutAsync"),
            ui.InvokeAsync(component, "SignOutAsync")
        };
        try
        {
            var html = await ui.HtmlAsync();
            Assert.Contains("Signing out...", html);
            Assert.Contains("disabled", html);
            Assert.Contains("aria-busy=\"true\"", html);
            Assert.Equal(1, ui.JavaScript.LogoutCalls);
            Assert.True(ui.Session.IsAuthenticated);
        }
        finally
        {
            pending.TrySetResult(new AuthApiResult { Ok = true, Status = 204 });
            await Task.WhenAll(clicks);
            await ui.DisposeAsync();
        }

        Assert.Equal(1, ui.JavaScript.LogoutCalls);
        Assert.False(ui.Session.IsAuthenticated);
        Assert.Equal("/", ui.Navigation.Destination);
        Assert.True(ui.Navigation.ForceLoad);
    }

    [Fact]
    public async Task LogoutJavaScriptCancellation_PreservesSessionAndShowsRetry()
    {
        var (ui, component) = await RenderNavAsync(
            (_, _) => Task.FromException<AuthApiResult>(new TaskCanceledException("browser JS timeout")));

        await ui.InvokeAsync(component, "SignOutAsync");

        Assert.True(ui.Session.IsAuthenticated);
        Assert.Null(ui.Navigation.Destination);
        var html = await ui.HtmlAsync();
        Assert.Contains("We could not sign you out. Please try again.", html);
        Assert.Contains("Try again", html);
        Assert.DoesNotContain("Signing out...", html); // pending state was reset

        ui.JavaScript.Handler = (_, _) => Task.FromResult(new AuthApiResult { Ok = true, Status = 204 });
        await ui.InvokeAsync(component, "SignOutAsync");

        Assert.False(ui.Session.IsAuthenticated);
        Assert.Equal("/", ui.Navigation.Destination);
        Assert.True(ui.Navigation.ForceLoad);
        Assert.Equal(2, ui.JavaScript.LogoutCalls);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task RetryAfterLogoutError_CompletesSignOut()
    {
        var (ui, component) = await RenderNavAsync((_, _) => Task.FromResult(
            new AuthApiResult { Ok = false, Status = 500 }));
        await ui.InvokeAsync(component, "SignOutAsync");
        Assert.Contains("We could not sign you out. Please try again.", await ui.HtmlAsync());

        ui.JavaScript.Handler = (_, _) => Task.FromResult(new AuthApiResult { Ok = true, Status = 204 });
        await ui.InvokeAsync(component, "SignOutAsync");

        Assert.False(ui.Session.IsAuthenticated);
        Assert.Equal("/", ui.Navigation.Destination);
        Assert.True(ui.Navigation.ForceLoad);
        Assert.DoesNotContain("We could not sign you out. Please try again.", await ui.HtmlAsync());
        await ui.DisposeAsync();
    }

    /// <summary>The alert must not compete for width with avatar and sign-out.</summary>
    [Fact]
    public void SignOutErrorFeedback_UsesSeparateFullWidthRow()
    {
        var razor = ReadSource("hotjob/Components/Layout/NavMenu.razor");
        var css = ReadSource("hotjob/wwwroot/css/NavMenu.css");
        var actions = ExtractDivBlock(razor, "<div class=\"nav-actions\">");

        Assert.DoesNotContain("nav-sign-out-error", actions);
        Assert.Contains("class=\"nav-feedback-row\"", razor);
        Assert.Contains("role=\"alert\"", razor);
        Assert.Contains("aria-label=\"Try again - sign out\"", razor);

        Assert.Contains("display: flex", ExtractCssRule(css, ".nav-feedback-row"));
        var chip = ExtractCssRule(css, ".nav-sign-out-error");
        Assert.Contains("flex-wrap: wrap", chip);
        Assert.Contains("max-width: 100%", chip);
        var retry = ExtractCssRule(css, ".nav-sign-out-retry");
        Assert.Contains("var(--hj-surface)", retry);
        Assert.DoesNotContain("#FFFFFF", retry);
    }

    private static string ReadSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "hotjob.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }

    private static string ExtractDivBlock(string source, string openTag)
    {
        var start = source.IndexOf(openTag, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing markup block: {openTag}");
        var depth = 0;
        for (var i = start; i < source.Length; i++)
        {
            if (source.AsSpan(i).StartsWith("<div", StringComparison.Ordinal))
            {
                depth++;
            }
            else if (source.AsSpan(i).StartsWith("</div>", StringComparison.Ordinal) && --depth == 0)
            {
                return source[start..(i + "</div>".Length)];
            }
        }
        throw new InvalidOperationException($"Unclosed markup block: {openTag}");
    }

    private static string ExtractCssRule(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing CSS rule: {selector}");
        var end = css.IndexOf('}', start);
        Assert.True(end > start, $"Unclosed CSS rule: {selector}");
        return css[start..(end + 1)];
    }
}
