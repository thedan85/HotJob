using hotjob.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.NotificationTests;

/// <summary>In-process fan-out used by Blazor Server circuits for notification badge refresh.</summary>
public sealed class NotificationRealtimeTests
{
    [Fact]
    public async Task Publish_NotifiesOnlySubscribersOfThatUser()
    {
        var realtime = new NotificationRealtime(NullLogger<NotificationRealtime>.Instance);
        var first = 0;
        var second = 0;
        var other = 0;
        using var firstSubscription = realtime.Subscribe("user-1", () =>
        {
            first++;
            return Task.CompletedTask;
        });
        using var secondSubscription = realtime.Subscribe("user-1", () =>
        {
            second++;
            return Task.CompletedTask;
        });
        using var otherSubscription = realtime.Subscribe("user-2", () =>
        {
            other++;
            return Task.CompletedTask;
        });

        await realtime.PublishAsync("user-1");

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(0, other);
    }

    [Fact]
    public async Task Dispose_StopsFutureNotifications()
    {
        var realtime = new NotificationRealtime(NullLogger<NotificationRealtime>.Instance);
        var calls = 0;
        var subscription = realtime.Subscribe("user-1", () =>
        {
            calls++;
            return Task.CompletedTask;
        });

        subscription.Dispose();
        await realtime.PublishAsync("user-1");

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ThrowingSubscriber_DoesNotBlockOtherSubscribers()
    {
        var realtime = new NotificationRealtime(NullLogger<NotificationRealtime>.Instance);
        var calls = 0;
        using var throwing = realtime.Subscribe("user-1", () => throw new InvalidOperationException("bad circuit"));
        using var healthy = realtime.Subscribe("user-1", () =>
        {
            calls++;
            return Task.CompletedTask;
        });

        await realtime.PublishAsync("user-1");

        Assert.Equal(1, calls);
    }
}
