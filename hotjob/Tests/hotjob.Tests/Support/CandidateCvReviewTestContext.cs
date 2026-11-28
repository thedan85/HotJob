using System.Reflection;
using hotjob.Components.Account;
using hotjob.Components.Pages;
using hotjob.DTOs.Authentication;
using hotjob.DTOs.Candidate;
using hotjob.Repositories.Employer;
using hotjob.Services.Account;
using hotjob.Services.Authentication;
using hotjob.Services.Billing;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Services.Employer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace hotjob.Tests.Support;

public sealed class ReviewOptions
{
    public bool AiAvailable { get; set; }
    public bool RenderProfile { get; set; }
    public string? Role { get; set; } = "Candidate";
    public bool SavedCallbackThrows { get; set; }
    public IReadOnlyList<SkillOptionDto>? Catalog { get; set; }
    public ServiceResult<ParsedResumeDto>? ParseResult { get; set; }
    public Func<ParsedCall, Task<ServiceResult<ParsedResumeDto>>>? ParseHandler { get; set; }
}

public sealed record ParsedCall(string? UserId, string FileName, ResumeParseMode Mode, CancellationToken Token);

/// <summary>Receives child EventCallbacks without a real circuit or parent page.</summary>
internal sealed class CallbackSink(bool throwOnSaved)
{
    public List<CvReviewSaveResult> SavedResults { get; } = [];
    public List<bool> BusyStates { get; } = [];
    public int CancelCount { get; private set; }

    public Task Saved(CvReviewSaveResult result)
    {
        SavedResults.Add(result);
        if (throwOnSaved) throw new InvalidOperationException("List refresh failed.");
        return Task.CompletedTask;
    }

    public Task BusyChanged(bool busy)
    {
        BusyStates.Add(busy);
        return Task.CompletedTask;
    }

    public Task Cancelled()
    {
        CancelCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// StaticHtmlRenderer host for CandidateCvReview with a fake parser/browser
/// file/storage and the real ResumeService over SQLite. No app startup, no
/// real AI and no user-data writes.
/// </summary>
internal sealed class ReviewPage : IAsyncDisposable
{
    private readonly TestFixture fixture;
    private readonly ServiceProvider services;
    private readonly ReviewRenderer renderer;
    private readonly CapturingActivator activator;
    private readonly HtmlRootComponent root;

    private ReviewPage(TestFixture fixture, ServiceProvider services, ReviewRenderer renderer,
        CapturingActivator activator, HtmlRootComponent root, string userId,
        IReadOnlyList<SkillOptionDto> catalog, CallbackSink sink)
    {
        this.fixture = fixture;
        this.services = services;
        this.renderer = renderer;
        this.activator = activator;
        this.root = root;
        UserId = userId;
        Catalog = catalog;
        Sink = sink;
    }

    public string UserId { get; }
    public IReadOnlyList<SkillOptionDto> Catalog { get; }
    public CallbackSink Sink { get; }
    public FakeBrowserFile? File { get; private set; }
    public CandidateCvReview? Child => activator.Child;
    public Profile? Profile => activator.Profile;
    public FakeResumeParserService Parser => (FakeResumeParserService)services.GetRequiredService<IResumeParserService>();
    public FakeCvFileStorage Storage => (FakeCvFileStorage)services.GetRequiredService<ICvFileStorage>();
    public CircuitDbGate Gate => services.GetRequiredService<CircuitDbGate>();
    public TestFixture Fixture => fixture;
    public List<string> SavedMessages => Sink.SavedResults.Select(result => result.Message).ToList();
    public List<CvReviewSaveResult> SavedResults => Sink.SavedResults;
    public List<bool> BusyStates => Sink.BusyStates;
    public int CancelCount => Sink.CancelCount;

    public ResumeReviewDraft? Draft => GetField<ResumeReviewDraft?>("draft");
    public EditContext? ReviewEditContext => GetField<EditContext?>("editContext");

    public Dispatcher Dispatcher => renderer.Dispatcher;

    public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);

    /// <summary>Invokes a private handler through a real ComponentBase EventCallback.</summary>
    public Task EventAsync(string name, params object?[] arguments) =>
        renderer.Dispatcher.InvokeAsync(() =>
            EventCallback.Factory.Create(activator.Child ?? (IComponent)activator.Profile!, async () =>
            {
                object target = activator.Child ?? (object)activator.Profile!;
                var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);
                if (method.Invoke(target, arguments) is Task task) await task;
            }).InvokeAsync());

    public Task ProfileEventAsync(string name, params object?[] arguments) =>
        renderer.Dispatcher.InvokeAsync(() =>
            EventCallback.Factory.Create(activator.Profile!, async () =>
            {
                var method = typeof(Profile).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);
                if (method.Invoke(activator.Profile, arguments) is Task task) await task;
            }).InvokeAsync());

    public IReadOnlyList<T> Captured<T>() where T : IComponent => activator.Components.OfType<T>().ToArray();

    /// <summary>
    /// Drives a captured InputBase through the same CurrentValueAsString parser that
    /// its DOM onchange callback uses, via a real EventCallback on the renderer dispatcher.
    /// </summary>
    public Task ChangeInputAsync<T>(string value, int index = -1) where T : IComponent
    {
        var matches = Captured<T>();
        Assert.NotEmpty(matches);
        var input = matches.ElementAt(index >= 0 ? index : matches.Count - 1);
        return renderer.Dispatcher.InvokeAsync(() =>
            EventCallback.Factory.Create<IComponent>(input, async _ =>
            {
                var property = typeof(InputBase<>).MakeGenericType(
                    input.GetType().BaseType!.GenericTypeArguments[0])!
                    .GetProperty("CurrentValueAsString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
                property.SetValue(input, value);
                await Task.CompletedTask;
            }).InvokeAsync(null));
    }

    public async Task SelectFileAsync(string name, byte[] bytes, long? reportedSize = null,
        string contentType = "application/pdf", int throwOnOpenNumber = 0)
    {
        var file = new FakeBrowserFile(name, bytes, reportedSize, contentType)
        {
            ThrowOnOpenNumber = throwOnOpenNumber
        };
        File = file;
        await EventAsync("OnFileSelected", new InputFileChangeEventArgs(new IBrowserFile[] { file }));
    }

    public T? GetField<T>(string name)
    {
        var target = (object?)activator.Child ?? activator.Profile!;
        return (T?)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);
    }

    public void SetField(string name, object? value)
    {
        var target = (object?)activator.Child ?? activator.Profile!;
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    public int ResumeCount => fixture.Db.Resumes.Count();

    public async Task<ResumeDetailDto?> StoredAsync(int id)
    {
        var result = await services.GetRequiredService<IResumeService>().GetAsync(UserId, id);
        Assert.True(result.Success, string.Join(" ", result.Errors));
        return result.Data;
    }

    public static async Task<ReviewPage> CreateAsync(ReviewOptions? options = null)
    {
        options ??= new ReviewOptions();
        var fx = await TestFixture.CreateAsync();
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "review@example.test");
        var catalog = options.Catalog?.ToList() ??
        [
            new SkillOptionDto(1, "C#", null), new SkillOptionDto(2, ".NET", null),
            new SkillOptionDto(3, "Java", null), new SkillOptionDto(4, "SQL", null)
        ];
        foreach (var skill in catalog) await MatchTestData.GetOrCreateSkillAsync(fx, skill.Name);

        var session = new AuthSession();
        if (options.Role is not null)
            session.SetTokens(new AuthTokens("test-access", DateTime.UtcNow.AddHours(1), "",
                DateTime.UtcNow, candidate.UserId, "review@example.test", [options.Role]));

        var activator = new CapturingActivator();
        var sink = new CallbackSink(options.SavedCallbackThrows);
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddAuthorizationCore();
        collection.AddCascadingAuthenticationState();
        collection.AddSingleton<IComponentActivator>(activator);
        collection.AddSingleton(session);
        collection.AddSingleton<HotJobAuthStateProvider>();
        collection.AddSingleton<AuthenticationStateProvider>(
            s => s.GetRequiredService<HotJobAuthStateProvider>());
        collection.AddSingleton<IAccountService>(fx.Service);
        collection.AddSingleton<ICandidatePremiumService>(new CandidatePremiumService(fx.Db, TimeProvider.System));
        collection.AddSingleton<CircuitDbGate>();
        collection.AddSingleton<FakeCvFileStorage>();
        collection.AddSingleton<ICvFileStorage>(s => s.GetRequiredService<FakeCvFileStorage>());
        collection.AddSingleton<IResumeService>(s => new ResumeService(
            fx.Db, s.GetRequiredService<FakeCvFileStorage>(), TimeProvider.System, NullLogger<ResumeService>.Instance));
        collection.AddSingleton<ICandidateSearchService>(new CandidateSearchService(
            fx.Db, new ApplicantRepository(fx.Db), TimeProvider.System));
        collection.AddSingleton<NavigationManager>(new ReviewNavigation());
        collection.AddSingleton<IJSRuntime>(new NoJavaScript());
        collection.AddSingleton<IResumeParserService>(new FakeResumeParserService
        {
            IsAiAvailable = options.AiAvailable,
            Result = options.ParseResult ?? ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed()),
            Handler = options.ParseHandler
        });

        var services = collection.BuildServiceProvider();
        var renderer = new ReviewRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var parameters = options.RenderProfile
            ? ParameterView.Empty
            : ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["SkillOptions"] = catalog,
                ["OnSaved"] = EventCallback.Factory.Create<CvReviewSaveResult>(sink, sink.Saved),
                ["OnBusyChanged"] = EventCallback.Factory.Create<bool>(sink, sink.BusyChanged),
                ["OnCancel"] = EventCallback.Factory.Create(sink, sink.Cancelled)
            });
        var root = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = renderer.BeginRenderingComponent(
                options.RenderProfile ? typeof(Profile) : typeof(CandidateCvReview), parameters);
            await rendered.QuiescenceTask;
            return rendered;
        });
        return new ReviewPage(fx, services, renderer, activator, root, candidate.UserId, catalog, sink);
    }

    public async ValueTask DisposeAsync()
    {
        await ((IAsyncDisposable)renderer).DisposeAsync();
        await services.DisposeAsync();
        await fixture.DisposeAsync();
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public CandidateCvReview? Child { get; private set; }
        public Profile? Profile { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            Components.Add(component);
            if (component is CandidateCvReview child) Child = child;
            if (component is Profile profile) Profile = profile;
            return component;
        }
    }

    private sealed class ReviewRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(
            Type componentType, int? parentComponentId, IComponentActivator componentActivator,
            IComponentRenderMode renderMode) => componentActivator.CreateInstance(componentType);
    }

    private sealed class ReviewNavigation : NavigationManager
    {
        public ReviewNavigation() => Initialize("http://localhost/", "http://localhost/profile");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}

internal static class ReviewSamples
{
    public static ParsedResumeDto Parsed() => new()
    {
        Summary = "Parsed summary",
        SummaryConfidence = ResumeParseConfidence.High,
        ModeUsed = ResumeParseMode.Rules,
        Warnings = ["A reversed date range was omitted."],
        UnrecognizedSkills = ["Rust"],
        Educations =
        [
            new(new()
            {
                SchoolName = "First University", Major = "Bachelor of Science - Computer Science",
                StartDate = new DateOnly(2017, 9, 1), EndDate = new DateOnly(2021, 6, 30),
                Gpa = 3.2, Description = "First education"
            }, "Bachelor of Science", "Computer Science", ResumeParseConfidence.Medium),
            new(new()
            {
                SchoolName = "Second University", Major = "Master of Science",
                StartDate = new DateOnly(2021, 9, 1), EndDate = new DateOnly(2023, 6, 30),
                Gpa = 3.8, Description = "Second education"
            }, "Master of Science", null, ResumeParseConfidence.Low)
        ],
        WorkExperiences =
        [
            new(new()
            {
                CompanyName = "First Company", JobTitle = "Developer",
                StartDate = new DateOnly(2021, 7, 1), EndDate = new DateOnly(2023, 12, 31),
                Description = "First work"
            }, false, ResumeParseConfidence.Low),
            new(new()
            {
                CompanyName = "Second Company", JobTitle = "Engineer",
                StartDate = new DateOnly(2024, 1, 1), EndDate = null,
                Description = "Second work"
            }, true, ResumeParseConfidence.Medium)
        ],
        Skills = [new(new() { SkillId = 1, YearsOfExperience = 1 }, "C#", ResumeParseConfidence.High)]
    };
}

internal sealed class FakeResumeParserService : IResumeParserService
{
    public bool IsAiAvailable { get; set; }
    public List<ParsedCall> Calls { get; } = [];
    public ServiceResult<ParsedResumeDto> Result { get; set; } = ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed());
    public Func<ParsedCall, Task<ServiceResult<ParsedResumeDto>>>? Handler { get; set; }

    public Task<ServiceResult<ParsedResumeDto>> ParseAsync(string? userId, Stream file, string fileName,
        ResumeParseMode mode = ResumeParseMode.Rules, CancellationToken ct = default)
    {
        Calls.Add(new ParsedCall(userId, fileName, mode, ct));
        return Handler is null ? Task.FromResult(Result) : Handler(this.Calls[^1]);
    }
}

internal sealed class FakeBrowserFile(
    string name, byte[] bytes, long? reportedSize = null, string contentType = "application/pdf") : IBrowserFile
{
    public string Name => name;
    public long Size => reportedSize ?? bytes.Length;
    public string ContentType => contentType;
    public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
    public int OpenCount { get; private set; }
    public int ThrowOnOpenNumber { get; init; }
    public Stream OpenReadStream(long maxAllowedFileSize = 512000, CancellationToken cancellationToken = default)
    {
        OpenCount++;
        if (ThrowOnOpenNumber == OpenCount) throw new IOException("The file could not be opened again.");
        return new MemoryStream(bytes, writable: false);
    }
}

internal sealed class FakeCvFileStorage : ICvFileStorage
{
    public int SaveCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public List<(string? Name, long Length, byte[] Bytes)> Saved { get; } = [];
    public Func<string?, long, Stream, Task<ServiceResult<string>>>? OnSave { get; set; }

    public async Task<ServiceResult<string>> SaveAsync(int candidateId, string? fileName, long length,
        Stream content, CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        if (OnSave is not null) return await OnSave(fileName, length, content);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Saved.Add((fileName, length, buffer.ToArray()));
        return ServiceResult<string>.Ok("/uploads/cv/test.pdf");
    }

    public Task DeleteAsync(string? relativeUrl, CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        return Task.CompletedTask;
    }
}
