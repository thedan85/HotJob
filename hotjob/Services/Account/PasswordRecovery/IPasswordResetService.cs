using hotjob.Services.Common;

namespace hotjob.Services.Account.PasswordRecovery;

public interface IPasswordResetService
{
    Task<ServiceResult<string>> RequestPasswordResetAsync(
        string? email, string baseUrl, CancellationToken cancellationToken = default);

    Task<ServiceResult> ResetPasswordAsync(
        string? email, string? token, string newPassword, CancellationToken cancellationToken = default);
}
