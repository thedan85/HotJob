using System.Net;
using HotJob.Dtos;
using hotjob.Services.Api;
using hotjob.Tests.ApiClients;

namespace hotjob.Tests;

public sealed class ApiClientBaseTests
{
    [Fact]
    public async Task Get_AttachesBearer_AndParsesEnvelope()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope("hello"));
        var scope = ApiStubs.Scope(handler);

        var result = await ApiStubs.RawClient(scope).Get<string>("api/employer/jobs/1");

        Assert.True(result.Success);
        Assert.Equal("hello", result.Data);
        Assert.Equal("Bearer", scope.Handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("access-1", scope.Handler.Requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Get_Unauthorized_RefreshesOnce_ThenRetries()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.Unauthorized, new { success = false });
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope("retry-ok"));
        var scope = ApiStubs.Scope(handler, () => ValueTask.FromResult(ApiStubs.RefreshOk("access-2")));

        var result = await ApiStubs.RawClient(scope).Get<string>("api/ping");

        Assert.True(result.Success);
        Assert.Equal("retry-ok", result.Data);
        Assert.Equal(2, scope.Handler.Requests.Count);
        Assert.Equal("access-2", scope.Handler.Requests[1].Headers.Authorization?.Parameter);
        Assert.Equal(1, scope.Js.Calls);
        Assert.Equal("access-2", scope.Session.Tokens!.AccessToken);
    }

    [Fact]
    public async Task Get_SecondUnauthorized_ReturnsUnauthorizedWithoutLooping()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.Unauthorized, new { success = false });
        handler.EnqueueJson(HttpStatusCode.Unauthorized, new { success = false });
        var scope = ApiStubs.Scope(handler, () => ValueTask.FromResult(ApiStubs.RefreshOk()));

        var result = await ApiStubs.RawClient(scope).Get<string>("api/ping");

        Assert.False(result.Success);
        Assert.Equal(HttpStatusCode.Unauthorized, result.Status);
        Assert.Equal(2, scope.Handler.Requests.Count);
    }

    [Fact]
    public async Task Get_NetworkFailure_ReturnsFriendlyMessage()
    {
        var factory = new StubFactory(new ThrowingHandler());
        var session = new hotjob.Services.Authentication.AuthSession();
        session.SetTokens(new hotjob.Services.Authentication.AuthTokens(
            "t", DateTime.UtcNow.AddHours(1), string.Empty, DateTime.UtcNow, "u", "e", ["Employer"]));
        var provider = new hotjob.Services.Authentication.HotJobAuthStateProvider(session);
        var client = new TestApiClient(factory, session, provider, new TestNav(), new StubJS());

        var result = await client.Get<string>("api/ping");

        Assert.False(result.Success);
        Assert.Contains("could not reach the server", string.Join(" ", result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_PrerenderJsUnavailable_NoRetry()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.Unauthorized, new { success = false });
        var scope = ApiStubs.Scope(handler, me: null);

        var result = await ApiStubs.RawClient(scope).Get<string>("api/ping");

        Assert.False(result.Success);
        Assert.Equal(HttpStatusCode.Unauthorized, result.Status);
        Assert.Single(scope.Handler.Requests);
    }
}
