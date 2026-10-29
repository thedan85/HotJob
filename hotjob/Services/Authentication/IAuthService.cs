using hotjob.DTOs.Authentication;
using hotjob.Services.Common;

namespace hotjob.Services.Authentication;

public interface IAuthService
{
    /// <summary>Validates credentials and issues an access JWT plus a persisted refresh token.</summary>
    Task<ServiceResult<AuthTokens>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs in a verified Google identity: links an existing email account or creates a
    /// Candidate account, then issues the same JWT + refresh pair as password login.
    /// </summary>
    Task<ServiceResult<AuthTokens>> LoginWithGoogleAsync(
        string? providerKey,
        string? email,
        string? displayName,
        CancellationToken cancellationToken = default);

    /// <summary>Rotates a refresh token: revokes the presented one and issues a new pair.</summary>
    Task<ServiceResult<AuthTokens>> RefreshAsync(
        string? rawRefreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a refresh cookie WITHOUT rotating or revoking anything and mints a fresh
    /// access token. Used by <c>GET /api/auth/me</c> to restore the UI session on circuit start.
    /// </summary>
    Task<ServiceResult<AuthTokens>> GetSessionAsync(
        string? rawRefreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes ALL refresh sessions for the user (logout everywhere).</summary>
    Task<ServiceResult> LogoutAsync(
        string? rawRefreshToken,
        string? currentUserId,
        CancellationToken cancellationToken = default);
}
