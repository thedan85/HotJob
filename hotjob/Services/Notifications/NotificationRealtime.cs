namespace hotjob.Services.Notifications;

/// <summary>
/// Best-effort subscriber registry. Callbacks are isolated: a dead or throwing
/// circuit cannot stop delivery to another circuit of the same user.
/// </summary>
public sealed class NotificationRealtime(ILogger<NotificationRealtime> logger) : INotificationRealtime
{
    private readonly object sync = new();
    private readonly Dictionary<string, HashSet<Subscription>> subscribers = new(StringComparer.Ordinal);

    public IDisposable Subscribe(string userId, Func<Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var subscription = new Subscription(this, userId, callback);
        lock (sync)
        {
            if (!subscribers.TryGetValue(userId, out var set))
            {
                set = new HashSet<Subscription>();
                subscribers[userId] = set;
            }

            set.Add(subscription);
        }

        return subscription;
    }

    public Task PublishAsync(string userId, CancellationToken cancellationToken = default)
    {
        Subscription[] snapshot;
        lock (sync)
        {
            if (!subscribers.TryGetValue(userId, out var set) || set.Count == 0)
            {
                return Task.CompletedTask;
            }

            snapshot = set.ToArray();
        }

        // Push is best-effort after the notification is committed. Never await a
        // circuit callback from the write path: it can be the same circuit and
        // hold the same DbContext gate that the write just used.
        foreach (var subscription in snapshot)
        {
            try
            {
                var callback = subscription.Callback();
                if (!callback.IsCompletedSuccessfully)
                {
                    _ = ObserveAsync(userId, callback);
                }
            }
            catch (Exception exception)
            {
                LogFailure(userId, exception);
            }
        }

        return Task.CompletedTask;
    }

    private async Task ObserveAsync(string userId, Task callback)
    {
        try
        {
            await callback.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogFailure(userId, exception);
        }
    }

    private void LogFailure(string userId, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            logger.LogDebug("Notification push to user {UserId} was canceled.", userId);
            return;
        }

        logger.LogWarning(exception, "Notification push to user {UserId} failed.", userId);
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (sync)
        {
            if (!subscribers.TryGetValue(subscription.UserId, out var set))
            {
                return;
            }

            set.Remove(subscription);
            if (set.Count == 0)
            {
                subscribers.Remove(subscription.UserId);
            }
        }
    }

    private sealed class Subscription(
        NotificationRealtime owner,
        string userId,
        Func<Task> callback) : IDisposable
    {
        private int disposed;

        public string UserId { get; } = userId;

        public Func<Task> Callback { get; } = callback;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Unsubscribe(this);
            }
        }
    }
}
