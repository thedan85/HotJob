using System.Globalization;
using System.Net;
using System.Text;
using hotjob.Models.Users;
using hotjob.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace hotjob.Services.Account.PasswordRecovery;

public sealed class PasswordResetDelivery(
    UserManager<ApplicationUser> userManager,
    IEmailSender sender,
    IOptions<PasswordResetOptions> options,
    ILogger<PasswordResetDelivery> logger)
{
    public async Task DeliverAsync(PasswordResetJob job, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var user = await userManager.FindByEmailAsync(job.Email);
            if (user is null || !user.IsActive || !await userManager.HasPasswordAsync(user)) return;

            var rawToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
            var url = WebUtility.HtmlEncode(PasswordResetLink.Build(job.BaseUrl, user.Email!, token));
            var lifetime = options.Value.TokenLifetime;
            var duration = lifetime == TimeSpan.FromHours(1)
                ? "1 hour"
                : $"{lifetime.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture)} minutes";
            var body = $"""
                <!doctype html><html lang="en"><body>
                <h1>Reset your HotJob password</h1>
                <p>We received a request to reset your password. This link expires in {WebUtility.HtmlEncode(duration)}.</p>
                <p><a href="{url}">Reset password</a></p>
                <p>You can also copy this link into your browser:</p><p>{url}</p>
                <p>If you did not request this, ignore this email. Your password will not change.</p>
                </body></html>
                """;
            await sender.SendAsync(user.Email!, "Reset your HotJob password", body, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning("Password recovery delivery failed ({FailureType}).", exception.GetType().Name);
        }
    }
}
