using System.ComponentModel.DataAnnotations;
using hotjob.Services.Common;
using hotjob.Data;
using hotjob.Models.Users;
using hotjob.Services.Authentication;
using Microsoft.AspNetCore.Identity;

namespace hotjob.Services.Account.PasswordRecovery;

public sealed partial class PasswordResetService(
    PasswordResetQueue queue,
    PasswordResetRateLimiter limiter,
    PasswordResetCallerContext caller,
    PasswordResetLink link,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    IAuthService authService,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    public const string RequestNotice =
        "If an active account with a local password matches that email, we will send a password reset link.";
    public const string InvalidLink = "This reset link is invalid or has expired";

    public Task<ServiceResult<string>> RequestPasswordResetAsync(
        string? email, string baseUrl, CancellationToken cancellationToken = default)
    {
        // No account lookup or SMTP on this path: response timing cannot branch on eligibility.
        var address = email?.Trim();
        if (!cancellationToken.IsCancellationRequested &&
            address is { Length: > 0 and <= 254 } && new EmailAddressAttribute().IsValid(address) &&
            limiter.TryAcquire(address, caller.IpAddress) && link.ResolveBaseUrl(baseUrl) is { } origin)
            queue.TryEnqueue(new PasswordResetJob(address, origin));

        return Task.FromResult(ServiceResult<string>.Ok(RequestNotice));
    }
}
