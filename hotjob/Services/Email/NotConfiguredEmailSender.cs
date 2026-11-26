namespace hotjob.Services.Email;

public sealed class NotConfiguredEmailSender(ILogger<NotConfiguredEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogWarning("Email delivery is not configured. Recovery email was not sent.");
        return Task.CompletedTask;
    }
}
