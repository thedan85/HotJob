using hotjob.Services.Notifications;

namespace hotjob.Tests.Support;

/// <summary>Records notification pushes without touching any circuit.</summary>
public sealed class RecordingBroadcaster : INotificationBroadcaster
{
    public List<string> UserIds { get; } = [];

    public Task NotifyAsync(string userId, string title, CancellationToken cancellationToken = default)
    {
        UserIds.Add(userId);
        return Task.CompletedTask;
    }
}
