using System.Security.Cryptography;
using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.Models.Admin;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

public sealed class AdminUserService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IAdminAuditService auditService,
    ILogger<AdminUserService> logger,
    TimeProvider? timeProvider = null) : IAdminUserService
{
    private const int RecentApplicationCount = 20;
    private const int RecentAuditCount = 10;
    private const int TemporaryPasswordLength = 16;

    // No look-alike characters (0/O, 1/l/I) so the password can be read out or typed by hand.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ServiceResult<AdminUserDetailDto>> GetDetailAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<AdminUserDetailDto>.Fail(ServiceErrorType.Validation, "User id is required.");

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return ServiceResult<AdminUserDetailDto>.Fail(ServiceErrorType.NotFound, "User not found.");

        var roles = await dbContext.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Join(dbContext.Roles, userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name!)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        var candidate = await LoadCandidateAsync(userId, cancellationToken);
        var employer = await LoadEmployerAsync(userId, cancellationToken);

        var audit = await dbContext.AdminAuditLogs
            .AsNoTracking()
            .Where(log => log.TargetType == AuditTargetTypes.User && log.TargetId == userId)
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Take(RecentAuditCount)
            .Select(log => new AdminAuditLogDto(
                log.Id, log.CreatedAt, log.AdminUserId, log.AdminEmail, log.Action, log.TargetType, log.TargetId, log.Detail))
            .ToListAsync(cancellationToken);

        return ServiceResult<AdminUserDetailDto>.Ok(new AdminUserDetailDto(
            user.Id,
            user.Email ?? string.Empty,
            candidate?.FullName ?? employer?.FullName,
            user.IsActive,
            user.EmailConfirmed,
            user.CreatedAt,
            roles,
            candidate,
            employer,
            audit));
    }

    public async Task<ServiceResult<AdminPasswordResetDto>> ResetPasswordAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<AdminPasswordResetDto>.Fail(ServiceErrorType.Validation, "User id is required.");

        // Throws for non-admins, same as the audit write would.
        var actingAdminId = await auditService.GetRequiredAdminIdAsync();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return ServiceResult<AdminPasswordResetDto>.Fail(ServiceErrorType.NotFound, "User not found.");
        if (user.Id == actingAdminId)
            return ServiceResult<AdminPasswordResetDto>.Fail(ServiceErrorType.Conflict, "Change your own password from your profile instead.");
        if (await userManager.IsInRoleAsync(user, AppRoles.Admin))
            return ServiceResult<AdminPasswordResetDto>.Fail(ServiceErrorType.Conflict, "Administrator passwords cannot be reset here.");

        var temporaryPassword = GenerateTemporaryPassword();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        IdentityResult? reset = null;

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            // Also rotates the security stamp.
            reset = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
            if (!reset.Succeeded)
                return;

            var now = clock.GetUtcNow().UtcDateTime;
            await dbContext.RefreshTokens
                .Where(refreshToken => refreshToken.UserId == user.Id && refreshToken.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(refreshToken => refreshToken.RevokedAtUtc, now)
                    .SetProperty(refreshToken => refreshToken.RevokedReason, "PasswordReset"), cancellationToken);

            await auditService.LogAsync(
                AuditActions.UserResetPassword,
                AuditTargetTypes.User,
                user.Id,
                $"Reset the password for {user.Email} and ended its sessions.");
        }, cancellationToken);

        if (reset is not { Succeeded: true })
        {
            return ServiceResult<AdminPasswordResetDto>.Fail(
                ServiceErrorType.Validation,
                reset?.Errors.Select(error => error.Description) ?? ["The password could not be reset."]);
        }

        logger.LogInformation("Admin {AdminId} reset the password of user {UserId}.", actingAdminId, user.Id);
        return ServiceResult<AdminPasswordResetDto>.Ok(new AdminPasswordResetDto(temporaryPassword));
    }

    private async Task<AdminCandidateSummaryDto?> LoadCandidateAsync(string userId, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.Candidates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        if (candidate is null)
            return null;

        var resumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.CandidateId == candidate.Id)
            .OrderByDescending(resume => resume.IsPrimary)
            .ThenByDescending(resume => resume.CreatedAt)
            .Select(resume => new AdminResumeSummaryDto(
                resume.Id,
                resume.Title,
                resume.IsPrimary,
                resume.FileUrl != null && resume.FileUrl != "",
                resume.CreatedAt))
            .ToListAsync(cancellationToken);

        var applications = dbContext.Applications
            .AsNoTracking()
            .Where(application => application.CandidateId == candidate.Id);
        var applicationCount = await applications.CountAsync(cancellationToken);
        var recent = await applications
            .OrderByDescending(application => application.AppliedAt)
            .ThenByDescending(application => application.Id)
            .Take(RecentApplicationCount)
            .Select(application => new AdminUserApplicationDto(
                application.Id,
                application.JobPostingId,
                application.JobPosting.Title,
                application.JobPosting.Company.CompanyName,
                application.CurrentStatus,
                application.AppliedAt))
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        return new AdminCandidateSummaryDto(
            candidate.Id,
            candidate.FullName,
            candidate.Position,
            candidate.PreferredLocation,
            candidate.JobSeekingStatus,
            candidate.PremiumUntil is { } until && until > now,
            candidate.PremiumUntil,
            candidate.IsDiscoverable,
            resumes,
            applicationCount,
            recent);
    }

    private Task<AdminEmployerSummaryDto?> LoadEmployerAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Employers
            .AsNoTracking()
            .Where(employer => employer.UserId == userId)
            .Select(employer => new AdminEmployerSummaryDto(
                employer.Id,
                employer.FullName,
                employer.Position,
                employer.CompanyProfileId,
                employer.Company.CompanyName,
                employer.MembershipStatus,
                employer.IsOwner,
                employer.IsActive,
                employer.CreatedJobs.Count))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Random password that always meets the Identity policy (upper, lower, digit, symbol).</summary>
    public static string GenerateTemporaryPassword()
    {
        const string all = Upper + Lower + Digits + Symbols;
        var chars = new char[TemporaryPasswordLength];
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];
        for (var i = 4; i < chars.Length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
