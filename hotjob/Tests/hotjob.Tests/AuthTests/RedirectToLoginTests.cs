using hotjob.Components;
using hotjob.DTOs.Authentication;
using hotjob.Services.Authentication;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace hotjob.Tests;

public sealed class RedirectToLoginTests
{
    private static AuthApiResult Anonymous() => new() { Ok = false, Status = 401 };

    private static AuthApiResult Restored(params string[] roles) => new()
    {
        Ok = true,
        Status = 200,
        Data = new AuthResponse("access", DateTime.UtcNow.AddHours(1),
            "user-1", "auth@example.test", roles)
    };

    private static async Task<(AuthUiTestContext Ui, RedirectToLogin Component)> ArrangeAsync(
        Func<string, object?[]?, Task<AuthApiResult>> handler, string[]? roles = null)
    {
        var ui = AuthUiTestContext.Create(new AuthUiOptions
        {
            Roles = roles,
            JavaScript = new AuthJavaScript { Handler = handler }
        });
        await ui.RenderAsync(typeof(CandidateBoundary));
        var component = ui.Single<RedirectToLogin>();
        return (ui, component);
    }

    [Fact]
    public async Task RestoredEmployer_ShowsAccessDeniedWithoutLoginRedirect()
    {
        var (ui, component) = await ArrangeAsync((_, _) => Task.FromResult(Restored("Employer")));

        await ui.AfterRenderAsync(component);

        var html = await ui.HtmlAsync();
        Assert.Contains("You do not have access to this page.", html);
        Assert.DoesNotContain("Checking your sign-in...", html);
        Assert.DoesNotContain("<form", html);
        Assert.Null(ui.Navigation.Destination);
        Assert.True(ui.Session.IsAuthenticated);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task AnonymousSession_RedirectsToLogin()
    {
        var (ui, component) = await ArrangeAsync((_, _) => Task.FromResult(Anonymous()));

        await ui.AfterRenderAsync(component);

        Assert.Equal("/login", ui.Navigation.Destination);
        Assert.False(ui.Session.IsAuthenticated);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task RestoredCandidateAndEmployer_RendersAuthorizedContent()
    {
        var (ui, component) = await ArrangeAsync((_, _) => Task.FromResult(Restored("Candidate", "Employer")));

        await ui.AfterRenderAsync(component);

        var html = await ui.HtmlAsync();
        Assert.Contains("candidate-only-content", html);
        Assert.DoesNotContain("You do not have access to this page.", html);
        Assert.DoesNotContain("Checking your sign-in...", html);
        Assert.Null(ui.Navigation.Destination);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task RestoredCandidate_RendersAuthorizedContent()
    {
        var (ui, component) = await ArrangeAsync((_, _) => Task.FromResult(Restored("Candidate")));

        await ui.AfterRenderAsync(component);

        var html = await ui.HtmlAsync();
        Assert.Contains("candidate-only-content", html);
        Assert.Null(ui.Navigation.Destination);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task AlreadyAuthenticatedEmployer_ShowsDeniedWithoutSessionCheck()
    {
        var (ui, component) = await ArrangeAsync(
            (id, _) => throw new InvalidOperationException($"Unexpected JS call: {id}"),
            ["Employer"]);

        await ui.AfterRenderAsync(component);

        Assert.Contains("You do not have access to this page.", await ui.HtmlAsync());
        Assert.Equal(0, ui.JavaScript.SessionChecks);
        Assert.Null(ui.Navigation.Destination);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task ServerFailure_ShowsAlertAndRetryWithoutRedirect()
    {
        var (ui, component) = await ArrangeAsync((_, _) => Task.FromResult(
            new AuthApiResult { Ok = false, Status = 500 }));

        await ui.AfterRenderAsync(component);

        var html = await ui.HtmlAsync();
        Assert.Contains("We could not check your sign-in. Please try again.", html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("<button", html);
        Assert.Contains("Try again", html);
        Assert.DoesNotContain("Checking your sign-in...", html);
        Assert.Null(ui.Navigation.Destination);
        Assert.False(ui.Session.IsAuthenticated);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task MalformedSuccess_ShowsAlertAndRetryWithoutRedirect()
    {
        var (ui, component) = await ArrangeAsync((_, _) => Task.FromResult(
            new AuthApiResult { Ok = true, Status = 200, Data = null }));

        await ui.AfterRenderAsync(component);

        var html = await ui.HtmlAsync();
        Assert.Contains("We could not check your sign-in. Please try again.", html);
        Assert.Contains("Try again", html);
        Assert.Null(ui.Navigation.Destination);
        Assert.False(ui.Session.IsAuthenticated);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task JavaScriptException_ShowsAlertAndRetryWithoutRedirect()
    {
        var (ui, component) = await ArrangeAsync(
            (_, _) => Task.FromException<AuthApiResult>(new JSException("bridge unavailable")));

        await ui.AfterRenderAsync(component);

        var html = await ui.HtmlAsync();
        Assert.Contains("We could not check your sign-in. Please try again.", html);
        Assert.Contains("Try again", html);
        Assert.Null(ui.Navigation.Destination);
        Assert.False(ui.Session.IsAuthenticated);
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task JavaScriptCancellation_ShowsAlertAndRetryWithoutRedirect()
    {
        var (ui, component) = await ArrangeAsync(
            (_, _) => Task.FromException<AuthApiResult>(new TaskCanceledException("browser JS timeout")));

        await ui.AfterRenderAsync(component);

        var html = await ui.HtmlAsync();
        Assert.Contains("We could not check your sign-in. Please try again.", html);
        Assert.Contains("Try again", html);
        Assert.Null(ui.Navigation.Destination);
        Assert.False(ui.Session.IsAuthenticated);

        // A working retry proves the failed check released its pending guard.
        ui.JavaScript.Handler = (_, _) => Task.FromResult(Anonymous());
        await ui.InvokeAsync(component, "RetryAsync");

        Assert.Equal("/login", ui.Navigation.Destination);
        Assert.Equal(2, ui.JavaScript.SessionChecks);
        Assert.DoesNotContain("We could not check your sign-in. Please try again.", await ui.HtmlAsync());
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task RetryAfterFailure_ResolvesSession()
    {
        var (ui, component) = await ArrangeAsync((_, _) => Task.FromResult(
            new AuthApiResult { Ok = false, Status = 500 }));
        await ui.AfterRenderAsync(component);
        Assert.Null(ui.Navigation.Destination);

        ui.JavaScript.Handler = (_, _) => Task.FromResult(Anonymous());
        await ui.InvokeAsync(component, "RetryAsync");

        Assert.Equal("/login", ui.Navigation.Destination);
        Assert.DoesNotContain("We could not check your sign-in. Please try again.", await ui.HtmlAsync());
        await ui.DisposeAsync();
    }

    [Fact]
    public async Task RepeatedRetry_WhilePending_IssuesOneSessionCheck()
    {
        var pending = new TaskCompletionSource<AuthApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (ui, component) = await ArrangeAsync((_, _) => pending.Task);

        // Retry callbacks stay pending until the session promise is released,
        // so queue them and observe via a later dispatcher callback.
        var checks = new[]
        {
            ui.AfterRenderAsync(component),
            ui.InvokeAsync(component, "RetryAsync"),
            ui.InvokeAsync(component, "RetryAsync")
        };
        try
        {
            await ui.HtmlAsync();
            Assert.Equal(1, ui.JavaScript.SessionChecks);
        }
        finally
        {
            pending.TrySetResult(Anonymous());
            await Task.WhenAll(checks);
            await ui.DisposeAsync();
        }

        Assert.Equal(1, ui.JavaScript.SessionChecks);
        Assert.Equal("/login", ui.Navigation.Destination);
    }

    /// <summary>The real Candidate authorization boundary around RedirectToLogin.</summary>
    private sealed class CandidateBoundary : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<AuthorizeView>(0);
            builder.AddAttribute(1, "Roles", "Candidate");
            builder.AddAttribute(2, "Authorized", (RenderFragment<AuthenticationState>)(_ => authorized =>
            {
                authorized.OpenElement(0, "div");
                authorized.AddAttribute(1, "id", "candidate-only-content");
                authorized.AddContent(2, "candidate-only-content");
                authorized.CloseElement();
            }));
            builder.AddAttribute(3, "NotAuthorized", (RenderFragment<AuthenticationState>)(_ => denied =>
            {
                denied.OpenComponent<RedirectToLogin>(0);
                denied.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
