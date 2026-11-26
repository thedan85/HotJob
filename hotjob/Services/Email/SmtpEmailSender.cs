using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace hotjob.Services.Email;

public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger,
    Func<SmtpClient>? createClient = null) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var client = createClient?.Invoke() ?? new SmtpClient();
            client.Timeout = 15_000;
            using var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(options.Value.From ?? ""));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            // Require STARTTLS, never downgrade to an unencrypted SMTP connection.
            // https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_ConnectAsync_2.htm
            await client.ConnectAsync(options.Value.Host ?? "", options.Value.Port, SecureSocketOptions.StartTls, deadline.Token);
            await client.AuthenticateAsync(options.Value.Username ?? "", options.Value.Password ?? "", deadline.Token);
            await client.SendAsync(message, deadline.Token);
            await client.DisconnectAsync(true, deadline.Token);
        }
        catch (Exception exception)
        {
            // SMTP exception text (including authentication responses) is untrusted.
            logger.LogWarning("SMTP email delivery failed ({FailureType}).", exception.GetType().Name);
        }
    }
}
