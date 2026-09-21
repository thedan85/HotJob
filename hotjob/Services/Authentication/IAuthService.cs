using hotjob.DTOs.Authentication;
using hotjob.Services.Common;

namespace hotjob.Services.Authentication;

public interface IAuthService
{
    /// <summary>Validates credentials and issues an access JWT plus a persisted refresh token.</summary>
    Task<ServiceResult<AuthTokens>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Rotates a refresh token: revokes the presented one and issues a new pair.</summary>
    Task<ServiceResult<AuthTokens>> RefreshAsync(
        string? rawRefreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes ALL refresh sessions for the user (logout everywhere).</summary>
    Task<ServiceResult> LogoutAsync(
        string? rawRefreshToken,
        string? currentUserId,
        CancellationToken cancellationToken = default);
}
