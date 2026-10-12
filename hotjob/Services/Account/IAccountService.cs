using hotjob.DTOs.Authentication;
using hotjob.Services.Common;

namespace hotjob.Services.Account;

public interface IAccountService
{
    Task<ServiceResult<RegisteredAccountResponse>> RegisterAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProfileResponse>> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProfileResponse>> UpdateProfileAsync(
        string userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ChangePasswordAsync(
        string userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}