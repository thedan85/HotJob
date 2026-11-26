namespace hotjob.Services.Notifications;

/// <summary>Pushes a saved notification to the target user's active circuits.</summary>
public interface INotificationBroadcaster
{
    Task NotifyAsync(string userId, string title, CancellationToken cancellationToken = default);
}
