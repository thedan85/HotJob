using hotjob.Data;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace hotjob.Services.Authentication;

/// <summary>Issued credential pair. The raw refresh token is returned once; only its hash is persisted.</summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string UserId,
    string Email,
    IReadOnlyList<string> Roles)
{
    public string? DisplayName { get; init; }
}

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    ITokenService tokenService,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
{
    private const string GoogleLoginProvider = "Google";
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<ServiceResult<AuthTokens>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Validation, "Email and password are required.");
        }

        // Generic message on purpose: no account-enumeration oracle.
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Invalid email or password.");
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Login blocked for disabled account {UserId}.", user.Id);
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Account is disabled.");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return ServiceResult<AuthTokens>.Fail(
                ServiceErrorType.Unauthorized, "Account is temporarily locked. Try again later.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Invalid email or password.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<ServiceResult<AuthTokens>> LoginWithGoogleAsync(
        string? providerKey,
        string? email,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return ServiceResult<AuthTokens>.Fail(
                ServiceErrorType.Validation, "Google did not return an account identifier.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return ServiceResult<AuthTokens>.Fail(
                ServiceErrorType.Validation, "Google did not return an email address.");
        }

        providerKey = providerKey.Trim();
        email = email.Trim();

        var user = await userManager.FindByLoginAsync(GoogleLoginProvider, providerKey);
        if (user is null)
        {
            user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                return await CreateGoogleUserAsync(providerKey, email, displayName, cancellationToken);
            }

            if (!user.IsActive)
            {
                logger.LogWarning("Google login blocked for disabled account {UserId}.", user.Id);
                return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Account is disabled.");
            }

            // Verified Google email matches an existing password account: attach the login.
            var linkResult = await userManager.AddLoginAsync(
                user, new UserLoginInfo(GoogleLoginProvider, providerKey, GoogleLoginProvider));
            if (!linkResult.Succeeded)
            {
                return ServiceResult<AuthTokens>.Fail(
                    ServiceErrorType.Conflict, "This Google account could not be linked to the existing account.");
            }
        }
        else if (!user.IsActive)
        {
            logger.LogWarning("Google login blocked for disabled account {UserId}.", user.Id);
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Account is disabled.");
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<ServiceResult<AuthTokens>> RefreshAsync(
        string? rawRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Refresh token is required.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hash = tokenService.HashToken(rawRefreshToken.Trim());

        var stored = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
        if (stored is null)
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Invalid refresh token.");
        }

        if (stored.RevokedAtUtc is not null)
        {
            // Reuse of a rotated token signals possible theft: revoke every session.
            await RevokeAllForUserAsync(stored.UserId, "ReuseDetected", now, cancellationToken);
            logger.LogWarning(
                "Refresh token reuse detected for user {UserId}. All sessions revoked.", stored.UserId);
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Invalid refresh token.");
        }

        if (stored.ExpiresAtUtc <= now)
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Refresh token has expired.");
        }

        var user = stored.User;
        if (user is null)
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Invalid refresh token.");
        }

        if (!user.IsActive)
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Account is disabled.");
        }

        // Rotation: revoke the presented token and link it to its replacement.
        var newRawToken = tokenService.GenerateRefreshToken();
        var newHash = tokenService.HashToken(newRawToken);
        stored.RevokedAtUtc = now;
        stored.RevokedReason = "Rotated";
        stored.ReplacedByTokenHash = newHash;

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.CreateAccessToken(user, roles, out var accessExpiresAtUtc);
        var displayName = await GetDisplayNameAsync(user.Id, cancellationToken);
        var refreshExpiresAtUtc = now.AddDays(_jwt.EffectiveRefreshTokenDays);
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = newHash,
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<AuthTokens>.Ok(new AuthTokens(
            accessToken, accessExpiresAtUtc, newRawToken, refreshExpiresAtUtc,
            user.Id, user.Email!, roles.AsReadOnly())
        {
            DisplayName = displayName
        });
    }

    public async Task<ServiceResult<AuthTokens>> GetSessionAsync(
        string? rawRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Not authenticated.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hash = tokenService.HashToken(rawRefreshToken.Trim());

        var stored = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
        if (stored is null || stored.RevokedAtUtc is not null)
        {
            // Read-only restore: no revoke-all side effect here (stale-tab cookie races
            // must not kill live sessions; reuse detection stays in RefreshAsync).
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Invalid refresh token.");
        }

        if (stored.ExpiresAtUtc <= now)
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Refresh token has expired.");
        }

        var user = stored.User;
        if (user is null)
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Invalid refresh token.");
        }

        if (!user.IsActive)
        {
            return ServiceResult<AuthTokens>.Fail(ServiceErrorType.Unauthorized, "Account is disabled.");
        }

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.CreateAccessToken(user, roles, out var accessExpiresAtUtc);
        var displayName = await GetDisplayNameAsync(user.Id, cancellationToken);
        return ServiceResult<AuthTokens>.Ok(new AuthTokens(
            accessToken, accessExpiresAtUtc, rawRefreshToken.Trim(), stored.ExpiresAtUtc,
            user.Id, user.Email!, roles.AsReadOnly())
        {
            DisplayName = displayName
        });
    }

    public async Task<ServiceResult> LogoutAsync(
        string? rawRefreshToken,
        string? currentUserId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserId;
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var hash = tokenService.HashToken(rawRefreshToken.Trim());
            var stored = await dbContext.RefreshTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
            if (stored is not null)
            {
                userId = stored.UserId;
            }
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult.Fail(ServiceErrorType.Unauthorized, "Not authenticated.");
        }

        await RevokeAllForUserAsync(userId, "Logout", timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult<AuthTokens>> CreateGoogleUserAsync(
        string providerKey,
        string email,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var fullName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim();
        if (fullName.Length > 100)
        {
            fullName = fullName[..100];
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsActive = true
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return IdentityFailure(createResult);
            }

            var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Candidate);
            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return IdentityFailure(roleResult);
            }

            var loginResult = await userManager.AddLoginAsync(
                user, new UserLoginInfo(GoogleLoginProvider, providerKey, GoogleLoginProvider));
            if (!loginResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return IdentityFailure(loginResult);
            }

            dbContext.Candidates.Add(new global::hotjob.Models.Users.Candidate
            {
                UserId = user.Id,
                FullName = fullName
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await IssueTokensAsync(user, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogWarning(exception, "Google account creation conflict for {Email}.", email);
            return ServiceResult<AuthTokens>.Fail(
                ServiceErrorType.Conflict, "An account with this email already exists.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Google account creation failed for {Email}.", email);
            throw;
        }
    }

    private static ServiceResult<AuthTokens> IdentityFailure(IdentityResult result)
    {
        var errors = result.Errors.ToArray();
        var errorType = errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail")
            ? ServiceErrorType.Conflict
            : ServiceErrorType.Validation;
        return ServiceResult<AuthTokens>.Fail(errorType, errors.Select(error => error.Description));
    }

    private async Task<ServiceResult<AuthTokens>> IssueTokensAsync(
        ApplicationUser user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.CreateAccessToken(user, roles, out var accessExpiresAtUtc);
        var displayName = await GetDisplayNameAsync(user.Id, cancellationToken);

        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var refreshExpiresAtUtc = now.AddDays(_jwt.EffectiveRefreshTokenDays);
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = tokenService.HashToken(rawRefreshToken),
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiresAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<AuthTokens>.Ok(new AuthTokens(
            accessToken, accessExpiresAtUtc, rawRefreshToken, refreshExpiresAtUtc,
            user.Id, user.Email!, roles.AsReadOnly())
        {
            DisplayName = displayName
        });
    }

    private async Task<string?> GetDisplayNameAsync(string userId, CancellationToken cancellationToken)
    {
        var employerName = await dbContext.Employers
            .Where(employer => employer.UserId == userId)
            .Select(employer => employer.FullName)
            .SingleOrDefaultAsync(cancellationToken);
        if (employerName is not null)
        {
            return employerName;
        }

        return await dbContext.Candidates
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => candidate.FullName)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task RevokeAllForUserAsync(
        string userId, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var active = await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in active)
        {
            token.RevokedAtUtc = now;
            token.RevokedReason = reason;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
