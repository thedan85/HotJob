using System.Net;
using System.Text.Json;
using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support.ResumeParsing;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class DeepSeekResumeClientTests
{
    internal static DeepSeekResumeClient Client(RecordingHttpHandler handler, bool enabled = true, string key = "test-only-key",
        TimeProvider? clock = null, ResumeAiUsageLimiter? limiter = null)
    {
        clock ??= TimeProvider.System;
        var options = Options.Create(new ResumeParsingOptions { UseAi = enabled });
        return new(handler, options, Options.Create(new DeepSeekOptions { ApiKey = key,
            BaseUrl = "https://example.test/v1", Model = "configured-test-model" }),
            new(clock), limiter ?? new(clock, options), clock);
    }
    [Fact]
    public async Task Success_UsesConfiguredProtocolAndRedactedText()
    {
        using var handler = new RecordingHttpHandler();
        var result = await Client(handler).ParseAsync("not-sent-user", "Skills: C#\nfake@example.test +84 912 345 678 https://example.test/fake", default);
        Assert.True(result.Success);
        Assert.Single(result.Data!.Skills);
        Assert.Equal("/v1/chat/completions", handler.LastUri!.AbsolutePath);
        Assert.Equal("Bearer test-only-key", handler.Authorization);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("configured-test-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("temperature").GetInt32());
        Assert.Equal(4096, body.RootElement.GetProperty("max_tokens").GetInt32());
        foreach (var value in new[] { "fake@example.test", "+84 912", "https://example.test/fake", "not-sent-user", "test-only-key" })
            Assert.DoesNotContain(value, handler.LastBody);
    }
    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(302)]
    public async Task HttpFailures_AreFixedAndNotRetried(int status)
    {
        using var handler = new RecordingHttpHandler { Respond = _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent("PROVIDER_SECRET_BODY") }) };
        var result = await Client(handler).ParseAsync("u", "Skills: C#", default);
        Assert.False(result.Success);
        Assert.DoesNotContain("PROVIDER_SECRET_BODY", string.Join(" ", result.Errors));
        Assert.Equal(1, handler.RequestCount);
    }
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"skills\":\"bad\"}")]
    [InlineData("{\"skills\":[],\"skills\":[]}")]
    [InlineData("{\"skills\":[]} trailing")]
    [InlineData("{\"skills\":[{\"id\":\"wrong\"}]}")]
    public async Task InvalidSchema_IsUnavailable(string inner)
    {
        using var handler = new RecordingHttpHandler { Respond = _ => Task.FromResult(RecordingHttpHandler.Json(inner)) };
        Assert.False((await Client(handler).ParseAsync("u", "Skills: C#", default)).Success);
    }
    [Theory]
    [InlineData(false, "key")]
    [InlineData(true, "")]
    public async Task DisabledOrNoKey_NoHttp(bool enabled, string key)
    {
        using var handler = new RecordingHttpHandler();
        var client = Client(handler, enabled, key);
        Assert.False(client.IsAiAvailable);
        Assert.False((await client.ParseAsync("u", "Skills: C#", default)).Success);
        Assert.Equal(0, handler.RequestCount);
    }
    [Fact]
    public async Task TimeoutAndCancellation_AreFriendly()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHttpHandler { Respond = async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return RecordingHttpHandler.Json("{}");
        }};
        var task = Client(handler, clock: clock).ParseAsync("u", "Skills: C#", default);
        await started.Task;
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False((await task).Success);
        Assert.Equal(1, handler.RequestCount);
        Assert.False((await Client(handler).ParseAsync("u", "Skills: C#", new(true))).Success);
        Assert.Equal(1, handler.RequestCount);
    }
}
