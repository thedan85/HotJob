using System.Security.Claims;
using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Models.Admin;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Admin;

/// <summary>
/// Records admin writes against the current administrator. API requests resolve the admin from
/// <see cref="HttpContext.User"/>; interactive Blazor circuits fall back to the authentication state.
/// </summary>
public sealed class AdminAuditService(
    ApplicationDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider) : IAdminAuditService
{
    private const int MaxDetailLength = 500;

    public async Task LogAsync(string action, string targetType, string targetId, string? detail = null)
    {
        var admin = await ResolveAdminAsync().ConfigureAwait(false);
        if (admin is null)
            throw new InvalidOperationException("An authenticated administrator ID and email are required for auditing.");

        dbContext.AdminAuditLogs.Add(new AdminAuditLog
        {
            AdminUserId = admin.Value.UserId,
            AdminEmail = admin.Value.Email,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = NormalizeDetail(detail)
        });

        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<ServiceResult<PagedResponse<AdminAuditLogDto>>> GetLogsAsync(
        int page,
        int pageSize,
        string? action = null,
        string? targetType = null,
        string? adminEmail = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = dbContext.AdminAuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var term = action.Trim();
            query = query.Where(log => log.Action == term);
        }

        if (!string.IsNullOrWhiteSpace(targetType))
        {
            var term = targetType.Trim();
            query = query.Where(log => log.TargetType == term);
        }

        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            var term = adminEmail.Trim();
            query = query.Where(log => EF.Functions.Like(log.AdminEmail, $"%{term}%"));
        }

        if (from is not null)
        {
            query = query.Where(log => log.CreatedAt >= from.Value);
        }

        if (to is not null)
        {
            // The UI filters by local calendar day; include every entry on the chosen end date.
            var toExclusive = to.Value.Date.AddDays(1);
            query = query.Where(log => log.CreatedAt < toExclusive);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
        {
            return ServiceResult<PagedResponse<AdminAuditLogDto>>.Ok(
                new PagedResponse<AdminAuditLogDto> { TotalCount = total, Page = page, PageSize = pageSize });
        }

        var items = await query
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(log => new AdminAuditLogDto(
                log.Id,
                log.CreatedAt,
                log.AdminUserId,
                log.AdminEmail,
                log.Action,
                log.TargetType,
                log.TargetId,
                log.Detail))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<PagedResponse<AdminAuditLogDto>>.Ok(new PagedResponse<AdminAuditLogDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    private async Task<(string UserId, string Email)?> ResolveAdminAsync()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            principal = state.User;
        }

        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
            return null;

        return (userId, email);
    }

    // Callers may supply concise reasons and names, never credentials, tokens,
    // connection strings or complete request bodies. Free text is not secret-scanned.
    private static string? NormalizeDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return null;

        var trimmed = detail.Trim();
        return trimmed.Length <= MaxDetailLength ? trimmed : trimmed[..MaxDetailLength];
    }
}
