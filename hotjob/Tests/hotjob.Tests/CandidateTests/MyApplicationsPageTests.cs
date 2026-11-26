using System.Reflection;
using hotjob.Components.Pages;
using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Job;
using hotjob.Models.Resume;
using hotjob.Models.Users;
using hotjob.Services.Authentication;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.CandidateTests;

/// <summary>
/// Renders the real My Applications page against SQLite and drives its private
/// handlers, matching the existing Premium/Profile page-test pattern.
/// </summary>
public sealed class MyApplicationsPageTests
{
    [Fact]
    public async Task Render_ShowsFirstPageWithPagerAndTotals()
    {
        await using var ui = await MyApplicationsPage.CreateAsync(applicationCount: 25);

        var html = await ui.HtmlAsync();

        Assert.Contains("Page 1 of 3", html);
        Assert.Contains("Showing 1–10 of 25", html);
        Assert.Contains("Job 24", html);
        Assert.DoesNotContain("Job 14", html);
    }

    [Fact]
    public async Task ChangePage_ShowsRequestedSlice()
    {
        await using var ui = await MyApplicationsPage.CreateAsync(applicationCount: 25);

        await ui.InvokeAsync("ChangePageAsync", 2);
        var html = await ui.HtmlAsync();

        Assert.Contains("Page 2 of 3", html);
        Assert.Contains("Showing 11–20 of 25", html);
        Assert.Contains("Job 14", html);
        Assert.DoesNotContain("Job 24", html);
    }

    [Fact]
    public async Task WithdrawOnlyItemOnLastPage_ReturnsToPreviousPage()
    {
        await using var ui = await MyApplicationsPage.CreateAsync(applicationCount: 21);
        await ui.SetStatusGroupAsync("active");
        await ui.InvokeAsync("ChangePageAsync", 3);

        Assert.Contains("Page 3 of 3", await ui.HtmlAsync());

        await ui.InvokeAsync("WithdrawAsync", ui.ApplicationIds[0]);
        var html = await ui.HtmlAsync();

        Assert.Contains("Page 2 of 2", html);
        Assert.Contains("Showing 11–20 of 20", html);
        Assert.DoesNotContain("Page 3 of", html);
    }

    [Fact]
    public async Task Render_NoApplications_ShowsEmptyState()
    {
        await using var ui = await MyApplicationsPage.CreateAsync(applicationCount: 0);

        var html = await ui.HtmlAsync();

        Assert.Contains("No applications yet", html);
        Assert.DoesNotContain("Page 1 of", html);
    }

    private sealed class MyApplicationsPage(
        TestFixture fixture,
        ServiceProvider services,
        PageRenderer renderer,
        HtmlRootComponent root,
        PageActivator activator,
        int[] applicationIds) : IAsyncDisposable
    {
        public int[] ApplicationIds => applicationIds;
        private MyApplications Page => activator.Page!;

        public static async Task<MyApplicationsPage> CreateAsync(int applicationCount)
        {
            var fixture = await TestFixture.CreateAsync();
            var user = await fixture.SeedUserAsync("page@example.com", "Str0ng!Pass", AppRoles.Candidate);
            var candidate = new Candidate { UserId = user.Id, FullName = "Page Candidate" };
            fixture.Db.Candidates.Add(candidate);
            await fixture.Db.SaveChangesAsync();
            var ids = await SeedApplicationsAsync(fixture, candidate.Id, applicationCount);

            var session = new AuthSession();
            session.SetTokens(new AuthTokens(
                "test-access", DateTime.UtcNow.AddHours(1), "", DateTime.UtcNow,
                user.Id, "page@example.com", ["Candidate"]));

            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddAuthorizationCore();
            collection.AddCascadingAuthenticationState();
            collection.AddSingleton(session);
            collection.AddSingleton<AuthenticationStateProvider, HotJobAuthStateProvider>();
            collection.AddSingleton<ICandidateService>(new CandidateService(fixture.Db));
            collection.AddSingleton<CircuitDbGate>();
            collection.AddSingleton<NotificationFeed>();
            collection.AddSingleton<IApplicationService>(new ApplicationService(
                fixture.Db, TimeProvider.System, NullLogger<ApplicationService>.Instance));
            var activator = new PageActivator();
            collection.AddSingleton<IComponentActivator>(activator);
            collection.AddSingleton<NavigationManager>(new PageNavigation());
            var services = collection.BuildServiceProvider();
            var renderer = new PageRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(
                () => renderer.BeginRenderingComponent(typeof(MyApplications), ParameterView.Empty));
            var page = new MyApplicationsPage(fixture, services, renderer, root, activator, ids);
            await page.SettleAsync();
            return page;
        }

        public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);

        public async Task SetStatusGroupAsync(string group)
        {
            await renderer.Dispatcher.InvokeAsync(() =>
            {
                var query = (MyApplicationsQuery)typeof(MyApplications)
                    .GetField("query", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(Page)!;
                query.StatusGroup = group;
            });
            await InvokeAsync("ApplyFiltersAsync");
        }

        public async Task InvokeAsync(string name, params object?[]? args)
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(MyApplications)
                    .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)method.Invoke(Page, args)!;
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(Page, null);
            });
            await SettleAsync();
        }

        public Task SettleAsync() => root.QuiescenceTask.WaitAsync(TimeSpan.FromSeconds(10));

        public async ValueTask DisposeAsync()
        {
            await ((IAsyncDisposable)renderer).DisposeAsync();
            await services.DisposeAsync();
            await fixture.DisposeAsync();
        }

        private static async Task<int[]> SeedApplicationsAsync(
            TestFixture fixture, int candidateId, int count)
        {
            var company = new CompanyProfile { CompanyName = "Page Co", Status = CompanyStatus.Approved };
            fixture.Db.CompanyProfiles.Add(company);
            await fixture.Db.SaveChangesAsync();
            var resume = new Resume { CandidateId = candidateId, Title = "CV" };
            fixture.Db.Resumes.Add(resume);
            await fixture.Db.SaveChangesAsync();

            var ids = new int[count];
            for (var index = 0; index < count; index++)
            {
                var job = new JobPosting
                {
                    CompanyProfileId = company.Id,
                    Title = $"Job {index}",
                    Description = "d",
                    Status = JobStatus.Published
                };
                fixture.Db.JobPostings.Add(job);
                await fixture.Db.SaveChangesAsync();

                var application = new Application
                {
                    JobPostingId = job.Id,
                    ResumeId = resume.Id,
                    CandidateId = candidateId,
                    CurrentStatus = ApplicationStatus.Pending,
                    AppliedAt = DateTime.UtcNow.AddMinutes(index)
                };
                fixture.Db.Applications.Add(application);
                await fixture.Db.SaveChangesAsync();
                ids[index] = application.Id;
            }

            return ids;
        }
    }

    private sealed class PageActivator : IComponentActivator
    {
        public MyApplications? Page { get; private set; }

        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is MyApplications page)
            {
                Page = page;
            }
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
        public PageNavigation() => Initialize("http://localhost/", "http://localhost/my-applications");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
