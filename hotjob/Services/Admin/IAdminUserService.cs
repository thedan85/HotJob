using hotjob.DTOs.Admin;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

/// <summary>Per-account admin views and actions (detail page, password reset).</summary>
public interface IAdminUserService
{
    /// <summary>Account, roles, candidate/employer profile summary and the latest admin actions on it.</summary>
    Task<ServiceResult<AdminUserDetailDto>> GetDetailAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the password with a random temporary one and ends every session. The temporary
    /// password is returned once and never stored or logged. Refuses the acting admin's own account
    /// and other administrator accounts.
    /// </summary>
    Task<ServiceResult<AdminPasswordResetDto>> ResetPasswordAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
