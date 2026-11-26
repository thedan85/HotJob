using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Admin;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

public sealed class EmployerAdminService(
    ApplicationDbContext dbContext,
    IAdminAuditService auditService,
    ILogger<EmployerAdminService> logger) : IEmployerAdminService
{
    private const int MaxReasonLength = 500;

    public async Task<ServiceResult<PagedResponse<EmployerListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Employers.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(e => e.MembershipStatus == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(e =>
                EF.Functions.Like(e.FullName, term) ||
                (e.User.Email != null && EF.Functions.Like(e.User.Email, term)) ||
                EF.Functions.Like(e.Company.CompanyName, term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.JoinedAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployerListItemDto(
                e.Id,
                e.UserId,
                e.FullName,
                e.Position,
                e.IsOwner,
                e.MembershipStatus.ToString(),
                e.IsActive,
                e.DeactivatedAt,
                e.User.Email,
                e.CompanyProfileId,
                e.Company.CompanyName))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<EmployerListItemDto>>.Ok(new PagedResponse<EmployerListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public Task<ServiceResult<PagedResponse<EmployerListItemDto>>> ListPendingAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default) =>
        ListAsync(page, pageSize, search, MembershipStatus.Pending, cancellationToken);

    public async Task<ServiceResult<EmployerDetailDto>> GetDetailAsync(
        int employerId,
        CancellationToken cancellationToken = default)
    {
        var employer = await dbContext.Employers
            .AsNoTracking()
            .Include(e => e.Company)
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Id == employerId, cancellationToken);

        return employer is null
            ? ServiceResult<EmployerDetailDto>.Fail(ServiceErrorType.NotFound, "Employer not found.")
            : ServiceResult<EmployerDetailDto>.Ok(ToDetail(employer));
    }

    public async Task<ServiceResult<EmployerDetailDto>> GetByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var employer = await dbContext.Employers
            .AsNoTracking()
            .Include(e => e.Company)
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

        return employer is null
            ? ServiceResult<EmployerDetailDto>.Fail(ServiceErrorType.NotFound, "Employer not found.")
            : ServiceResult<EmployerDetailDto>.Ok(ToDetail(employer));
    }

    public async Task<ServiceResult> ApproveAsync(
        int employerId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var employer = await FindAsync(employerId, cancellationToken);
        if (employer is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Employer not found.");
        if (employer.MembershipStatus != MembershipStatus.Pending)
            return ServiceResult.Fail(ServiceErrorType.Conflict, $"Only pending employers can be approved (current status: {employer.MembershipStatus}).");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            employer.MembershipStatus = MembershipStatus.Active;
            employer.IsActive = true;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.EmployerApprove,
                AuditTargetTypes.Employer,
                employer.Id.ToString(),
                $"Approved employer '{employer.FullName}' for '{employer.Company.CompanyName}'.");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} approved employer {EmployerId}.", adminUserId, employerId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectAsync(
        int employerId,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ServiceResult.Fail(ServiceErrorType.Validation, "A rejection reason is required.");
        if (trimmed.Length > MaxReasonLength)
            return ServiceResult.Fail(ServiceErrorType.Validation, $"The reason must be at most {MaxReasonLength} characters.");

        var employer = await FindAsync(employerId, cancellationToken);
        if (employer is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Employer not found.");
        if (employer.MembershipStatus != MembershipStatus.Pending)
            return ServiceResult.Fail(ServiceErrorType.Conflict, $"Only pending employers can be rejected (current status: {employer.MembershipStatus}).");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            employer.MembershipStatus = MembershipStatus.Rejected;
            employer.IsActive = false;
            employer.DeactivatedAt = DateTime.UtcNow;
            employer.DeactivatedByAdminId = adminUserId;
            employer.DeactivationReason = trimmed;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.EmployerReject,
                AuditTargetTypes.Employer,
                employer.Id.ToString(),
                $"Rejected employer '{employer.FullName}' for '{employer.Company.CompanyName}': {trimmed}");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} rejected employer {EmployerId}. Reason: {Reason}", adminUserId, employerId, trimmed);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeactivateAsync(
        int employerId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed?.Length > MaxReasonLength)
            return ServiceResult.Fail(ServiceErrorType.Validation, $"The reason must be at most {MaxReasonLength} characters.");

        var employer = await FindAsync(employerId, cancellationToken);
        if (employer is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Employer not found.");
        if (!employer.IsActive)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Employer is already deactivated.");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            employer.IsActive = false;
            employer.DeactivatedAt = DateTime.UtcNow;
            employer.DeactivatedByAdminId = adminUserId;
            employer.DeactivationReason = trimmed;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.EmployerDeactivate,
                AuditTargetTypes.Employer,
                employer.Id.ToString(),
                trimmed is null
                    ? $"Deactivated employer '{employer.FullName}'."
                    : $"Deactivated employer '{employer.FullName}': {trimmed}");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} deactivated employer {EmployerId}.", adminUserId, employerId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ReactivateAsync(
        int employerId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var employer = await FindAsync(employerId, cancellationToken);
        if (employer is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Employer not found.");
        if (employer.IsActive)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Employer is already active.");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            employer.IsActive = true;
            employer.DeactivatedAt = null;
            employer.DeactivatedByAdminId = null;
            employer.DeactivationReason = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.EmployerReactivate,
                AuditTargetTypes.Employer,
                employer.Id.ToString(),
                $"Reactivated employer '{employer.FullName}'.");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} reactivated employer {EmployerId}.", adminUserId, employerId);
        return ServiceResult.Ok();
    }

    private Task<hotjob.Models.Users.Employer?> FindAsync(int employerId, CancellationToken cancellationToken) =>
        dbContext.Employers
            .Include(e => e.Company)
            .FirstOrDefaultAsync(e => e.Id == employerId, cancellationToken);

    private static EmployerDetailDto ToDetail(hotjob.Models.Users.Employer employer) => new(
        employer.Id,
        employer.UserId,
        employer.User.Email ?? string.Empty,
        employer.FullName,
        employer.Position,
        employer.IsOwner,
        employer.MembershipStatus.ToString(),
        employer.IsActive,
        employer.JoinedAt,
        employer.DeactivatedAt,
        employer.DeactivationReason,
        employer.CompanyProfileId,
        employer.Company?.CompanyName);
}
