namespace hotjob.Services.Notifications;

/// <summary>In-process fan-out over the Blazor Server SignalR circuit.</summary>
public interface INotificationRealtime
{
    IDisposable Subscribe(string userId, Func<Task> callback);

    Task PublishAsync(string userId, CancellationToken cancellationToken = default);
}
