using System.Net;
using hotjob.Services.Account.PasswordRecovery;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace hotjob.Tests.PasswordRecoveryTests;

public sealed class RequestInfrastructureTests
{
    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Queue_IsBoundedAndNeverWaitsForAdmission()
    {
        var queue = new PasswordResetQueue();
        for (var i = 0; i < 100; i++)
            Assert.True(queue.TryEnqueue(new PasswordResetJob($"{i}@example.test", "https://example.test/")));
        Assert.False(queue.TryEnqueue(new PasswordResetJob("full@example.test", "https://example.test/")));
        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.True(queue.TryEnqueue(new PasswordResetJob("again@example.test", "https://example.test/")));
        queue.Complete();
        Assert.False(queue.TryEnqueue(new PasswordResetJob("closed@example.test", "https://example.test/")));
    }

    [Fact]
    public void EmailCooldown_IsNormalizedAndExpiresAfter60Seconds()
    {
        var time = Clock();
        var limiter = new PasswordResetRateLimiter(time);
        Assert.True(limiter.TryAcquire(" User@example.test ", "192.0.2.1"));
        Assert.False(limiter.TryAcquire("USER@example.test", "192.0.2.2"));
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(limiter.TryAcquire("user@example.test", "192.0.2.3"));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAcquire("user@example.test", "192.0.2.4"));
    }

    [Fact]
    public void IpBudget_IsChargedEvenWhenEmailIsCoolingDownAndResetsAfter10Minutes()
    {
        var time = Clock();
        var limiter = new PasswordResetRateLimiter(time);
        Assert.True(limiter.TryAcquire("same@example.test", "192.0.2.1"));
        for (var i = 0; i < 9; i++)
            Assert.False(limiter.TryAcquire("same@example.test", "::ffff:192.0.2.1"));
        Assert.False(limiter.TryAcquire("new@example.test", "192.0.2.1"));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(limiter.TryAcquire("new@example.test", "192.0.2.1"));
    }

    [Fact]
    public void Capacity_FailsClosedAndReclaimsExpiredEntries()
    {
        var time = Clock();
        var limiter = new PasswordResetRateLimiter(time, maximumEntries: 2);
        Assert.True(limiter.TryAcquire("a@example.test", "192.0.2.1"));
        Assert.True(limiter.TryAcquire("b@example.test", "192.0.2.2"));
        Assert.False(limiter.TryAcquire("c@example.test", "192.0.2.3"));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(limiter.TryAcquire("c@example.test", "192.0.2.3"));
    }

    [Fact]
    public void IpWindow_ExpiresEvenBetweenPeriodicCleanupTicks()
    {
        var time = Clock();
        var limiter = new PasswordResetRateLimiter(time);
        for (var i = 0; i < 10; i++)
            Assert.True(limiter.TryAcquire($"first-{i}@example.test", "192.0.2.1"));
        time.Advance(TimeSpan.FromMinutes(9.5));
        Assert.True(limiter.TryAcquire("other@example.test", "192.0.2.2"));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(limiter.TryAcquire("after-expiry@example.test", "192.0.2.1"));
    }

    [Fact]
    public async Task Admission_IsAtomicUnderConcurrentAttempts()
    {
        var limiter = new PasswordResetRateLimiter(Clock());
        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => limiter.TryAcquire("same@example.test", $"192.0.2.{i + 1}"))));
        Assert.Single(results, result => result);
    }

    [Fact]
    public async Task Caller_RetainsServerIpAcrossCircuitEventsAndIgnoresForwardedHeaders()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.1");
        http.Request.Headers["X-Forwarded-For"] = "192.0.2.99";
        var accessor = new HttpContextAccessor { HttpContext = http };
        var caller = new PasswordResetCallerContext(accessor);
        var handler = new PasswordResetCircuitHandler(caller);
        await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        accessor.HttpContext = null;
        await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.Equal("192.0.2.1", caller.IpAddress);
    }

    [Fact]
    public void MissingIp_UsesOneSharedLimitedBucket()
    {
        var limiter = new PasswordResetRateLimiter(Clock());
        for (var i = 0; i < 10; i++)
        {
            var caller = new PasswordResetCallerContext(new HttpContextAccessor());
            Assert.Equal("unknown", caller.IpAddress);
            Assert.True(limiter.TryAcquire($"{i}@example.test", caller.IpAddress));
        }
        Assert.False(limiter.TryAcquire("last@example.test", "unknown"));
    }

    [Theory]
    [InlineData(null, "http://localhost:5168/", "http://localhost:5168/")]
    [InlineData("https://public.example/app", "http://untrusted.test/", "https://public.example/app/")]
    [InlineData("javascript:alert(1)", "https://fallback.test/", null)]
    [InlineData("https://user:pass@example.test", "https://fallback.test/", null)]
    [InlineData("https://example.test/?x=1", "https://fallback.test/", null)]
    [InlineData("https://example.test/#fragment", "https://fallback.test/", null)]
    public void BaseUrl_PrefersConfiguredValueAndRejectsUnsafeUris(string? configured, string fallback, string? expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["App:BaseUrl"] = configured }).Build();
        Assert.Equal(expected, new PasswordResetLink(config).ResolveBaseUrl(fallback));
    }

    [Fact]
    public void Link_EncodesEmailAndTokenWithoutChangingApplicationPath()
    {
        var link = PasswordResetLink.Build("https://example.test/app/", "a+b&c@example.test", "token+&=");
        Assert.Equal("https://example.test/app/reset-password?email=a%2Bb%26c%40example.test&token=token%2B%26%3D", link);
    }
}
