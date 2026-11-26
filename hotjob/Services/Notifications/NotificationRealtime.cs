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

    public async Task PublishAsync(string userId, CancellationToken cancellationToken = default)
    {
        Subscription[] snapshot;
        lock (sync)
        {
            if (!subscribers.TryGetValue(userId, out var set) || set.Count == 0)
            {
                return;
            }

            snapshot = set.ToArray();
        }

        foreach (var subscription in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await subscription.Callback();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Notification push to user {UserId} failed.", userId);
            }
        }
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
