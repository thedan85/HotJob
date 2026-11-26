namespace hotjob.Services.Notifications;

/// <summary>Bridges saved notifications to active circuits without failing the write.</summary>
public sealed class NotificationBroadcaster(
    INotificationRealtime realtime,
    ILogger<NotificationBroadcaster> logger) : INotificationBroadcaster
{
    public async Task NotifyAsync(
        string userId,
        string title,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Pushing notification {Title} to user {UserId}.", title, userId);
            await realtime.PublishAsync(userId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Notification push to user {UserId} failed.", userId);
        }
    }
}
