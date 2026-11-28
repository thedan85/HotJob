using System.Text;
using hotjob.Models.Users;
using hotjob.Services.Email;
using hotjob.Services.Email.Templates;
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
            var url = PasswordResetLink.Build(job.BaseUrl, user.Email!, token);
            var email = PasswordResetTemplate.Render(
                new PasswordResetEmailModel(url, options.Value.TokenLifetime));
            await sender.SendAsync(user.Email!, email.Subject, email.Html, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning("Password recovery delivery failed ({FailureType}).", exception.GetType().Name);
        }
    }
}
