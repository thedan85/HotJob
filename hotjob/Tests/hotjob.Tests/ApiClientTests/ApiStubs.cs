using System.Net;
using System.Text;
using System.Text.Json;
using hotjob.Components;
using hotjob.Services.Api;
using hotjob.Services.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace hotjob.Tests.ApiClients;

internal sealed class QueueHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public readonly List<HttpRequestMessage> Requests = new();
    private readonly Queue<HttpResponseMessage> _responses = new();

    public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

    public void EnqueueJson(HttpStatusCode status, object? body) =>
        Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
        });

    public void EnqueueRaw(HttpStatusCode status, string body) =>
        Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No stubbed response queued.");
        }

        return Task.FromResult(_responses.Dequeue());
    }
}

internal sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("no such host");
}

internal sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };
}

internal sealed class TestNav : NavigationManager
{
    public TestNav() => Initialize("http://localhost/", "http://localhost/");

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
    }
}

internal sealed class StubJS : IJSRuntime
{
    public Func<ValueTask<AuthApiResult>>? MeHandler;
    public int Calls;

    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls++;
        if (MeHandler is not null)
        {
            var result = await MeHandler();
            return (TValue)(object)result;
        }

        throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}

internal sealed class TestApiClient(
    IHttpClientFactory factory,
    AuthSession session,
    HotJobAuthStateProvider provider,
    NavigationManager nav,
    IJSRuntime js) : ApiClientBase(factory, session, provider, nav, js)
{
    public Task<ApiResult<T>> Get<T>(string path, bool anonymous = false) =>
        GetAsync<T>(path, default, anonymous);

    public Task<ApiResult<TRes>> Post<TReq, TRes>(string path, TReq body, bool anonymous = false) =>
        PostAsync<TReq, TRes>(path, body, default, anonymous);
}

internal sealed record ApiScope(
    QueueHandler Handler,
    StubJS Js,
    AuthSession Session,
    HotJobAuthStateProvider Provider,
    TestNav Nav,
    StubFactory Factory)
{
    public EmployerJobsApiClient Jobs() => new(Factory, Session, Provider, Nav, Js);
    public EmployerApplicantsApiClient Applicants() => new(Factory, Session, Provider, Nav, Js);
    public EmployerInterviewsApiClient Interviews() => new(Factory, Session, Provider, Nav, Js);
    public AccountApiClient Account() => new(Factory, Session, Provider, Nav, Js);
    public CompanyApiClient Company() => new(Factory, Session, Provider, Nav, Js);
}

internal static class ApiStubs
{
    public static ApiScope Scope(
        QueueHandler? handler = null,
        Func<ValueTask<AuthApiResult>>? me = null,
        string accessToken = "access-1")
    {
        handler ??= new QueueHandler();
        var session = new AuthSession();
        session.SetTokens(new AuthTokens(
            accessToken,
            DateTime.UtcNow.AddHours(1),
            string.Empty,
            DateTime.UtcNow,
            "user-1",
            "user@example.com",
            ["Employer"]));
        var provider = new HotJobAuthStateProvider(session);
        var js = new StubJS { MeHandler = me };
        var nav = new TestNav();
        return new ApiScope(handler, js, session, provider, nav, new StubFactory(handler));
    }

    public static TestApiClient RawClient(ApiScope scope) =>
        new(scope.Factory, scope.Session, scope.Provider, scope.Nav, scope.Js);

    public static AuthApiResult RefreshOk(string accessToken = "access-2") =>
        new()
        {
            Ok = true,
            Status = 200,
            Data = new hotjob.DTOs.Authentication.AuthResponse(
                accessToken,
                DateTime.UtcNow.AddHours(1),
                "user-1",
                "user@example.com",
                ["Employer"],
                "HR")
        };

    public static HotJob.Dtos.ApiResponse<T> Envelope<T>(T? data) =>
        new() { Success = true, Data = data };
}
