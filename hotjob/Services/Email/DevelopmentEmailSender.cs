namespace hotjob.Services.Email;

public sealed class DevelopmentEmailSender(
    IHostEnvironment environment,
    ILogger<DevelopmentEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Defense in depth: even direct construction must never log links outside Development.
        if (environment.IsDevelopment())
            logger.LogInformation("Development email to {To}, subject {Subject}:\n{HtmlBody}", to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
