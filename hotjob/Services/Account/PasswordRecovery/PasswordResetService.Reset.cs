using System.Text;
using System.Data;
using hotjob.Services.Common;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Account.PasswordRecovery;

public sealed partial class PasswordResetService
{
    public const string TemporaryFailure = "We could not reset your password right now. Please try again.";

    public async Task<ServiceResult> ResetPasswordAsync(
        string? email, string? token, string newPassword, CancellationToken cancellationToken = default)
    {
        var address = email?.Trim();
        if (address is not { Length: > 0 and <= 254 } || token is not { Length: > 0 and <= 4096 })
            return Invalid();
        string decoded;
        try
        {
            decoded = new UTF8Encoding(false, true).GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            return Invalid();
        }

        try
        {
            // InnoDB SERIALIZABLE takes shared read locks until commit. An admin disable
            // cannot race this read and then be overwritten by Identity's whole-user update.
            // https://docs.oracle.com/cd/E17952_01/mysql-8.0-en/innodb-transaction-isolation-levels.html#isolevel_serializable
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var user = await userManager.FindByEmailAsync(address);
            // Circuits may already track a user changed by an administrator in another scope.
            if (user is not null) await dbContext.Entry(user).ReloadAsync(cancellationToken);
            if (user is null || !user.IsActive || !await userManager.HasPasswordAsync(user))
                return Invalid();

            // Identity verifies the protected token, validates policy, and rotates the stamp.
            // https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/UserManager.cs
            var result = await userManager.ResetPasswordAsync(user, decoded, newPassword);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                // A failed optimistic-concurrency write can leave a new hash/stamp tracked.
                dbContext.ChangeTracker.Clear();
                if (result.Errors.Any(error => error.Code is "InvalidToken" or "ConcurrencyFailure"))
                    return Invalid();
                var policyErrors = result.Errors.Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
                    .Select(error => error.Description).ToArray();
                return policyErrors.Length > 0
                    ? ServiceResult.Fail(ServiceErrorType.Validation, policyErrors)
                    : Invalid();
            }

            var revoked = await authService.LogoutAsync(null, user.Id, cancellationToken);
            if (!revoked.Success) throw new InvalidOperationException("Refresh session revocation failed.");
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult.Ok();
        }
        catch (Exception exception)
        {
            // The transaction has been disposed/rolled back before clearing tracked changes.
            dbContext.ChangeTracker.Clear();
            logger.LogWarning("Password reset failed ({FailureType}).", exception.GetType().Name);
            return ServiceResult.Fail(ServiceErrorType.Validation, TemporaryFailure);
        }
    }

    private static ServiceResult Invalid() => ServiceResult.Fail(ServiceErrorType.Validation, InvalidLink);
}
