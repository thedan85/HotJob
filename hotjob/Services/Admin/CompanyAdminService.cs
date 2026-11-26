using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Admin;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

public sealed class CompanyAdminService(
    ApplicationDbContext dbContext,
    IAdminAuditService auditService,
    ILogger<CompanyAdminService> logger) : ICompanyAdminService
{
    private const int MaxReasonLength = 500;

    public async Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CompanyStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.CompanyProfiles.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(c => c.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.Like(c.CompanyName, term) ||
                (c.Industry != null && EF.Functions.Like(c.Industry, term)) ||
                (c.Location != null && EF.Functions.Like(c.Location, term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            // Pending first so the review queue is always on page 1, then alphabetical.
            .OrderBy(c => c.Status == CompanyStatus.Pending ? 0 : 1)
            .ThenBy(c => c.CompanyName)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CompanyListItemDto(
                c.Id,
                c.CompanyName,
                c.Industry,
                c.Location,
                c.LogoUrl,
                c.Status.ToString(),
                c.RejectionReason,
                c.VerifiedAt,
                c.IsActive,
                c.Employers.Count))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<CompanyListItemDto>>.Ok(new PagedResponse<CompanyListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListPendingAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default) =>
        ListAsync(page, pageSize, search, CompanyStatus.Pending, cancellationToken);

    public async Task<ServiceResult<CompanyDetailDto>> GetDetailAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var company = await dbContext.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
            return ServiceResult<CompanyDetailDto>.Fail(ServiceErrorType.NotFound, "Company not found.");

        return ServiceResult<CompanyDetailDto>.Ok(new CompanyDetailDto(
            company.Id,
            company.CompanyName,
            company.Website,
            company.LogoUrl,
            company.Location,
            company.Industry,
            company.Scale.HasValue ? (int)company.Scale.Value : null,
            company.Description,
            company.Status.ToString(),
            company.RejectionReason,
            company.VerifiedAt,
            company.VerifiedByAdminId,
            company.IsActive,
            company.DeactivatedAt,
            null));
    }

    public async Task<ServiceResult> ApproveAsync(
        int companyId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var company = await FindAsync(companyId, cancellationToken);
        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Company not found.");
        if (company.Status == CompanyStatus.Approved)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Company is already approved.");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            company.Status = CompanyStatus.Approved;
            company.VerifiedAt = DateTime.UtcNow;
            company.VerifiedByAdminId = adminUserId;
            company.RejectionReason = null;
            company.IsActive = true;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.CompanyApprove,
                AuditTargetTypes.Company,
                company.Id.ToString(),
                $"Approved company '{company.CompanyName}'.");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} approved company {CompanyId}.", adminUserId, companyId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectAsync(
        int companyId,
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

        var company = await FindAsync(companyId, cancellationToken);
        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Company not found.");
        if (company.Status == CompanyStatus.Rejected)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Company is already rejected.");

        // Rejecting an Approved company revokes its approval (kept from the original admin flow).
        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            company.Status = CompanyStatus.Rejected;
            company.RejectionReason = trimmed;
            company.VerifiedAt = DateTime.UtcNow;
            company.VerifiedByAdminId = adminUserId;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.CompanyReject,
                AuditTargetTypes.Company,
                company.Id.ToString(),
                $"Rejected company '{company.CompanyName}': {trimmed}");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} rejected company {CompanyId}. Reason: {Reason}", adminUserId, companyId, trimmed);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeactivateAsync(
        int companyId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed?.Length > MaxReasonLength)
            return ServiceResult.Fail(ServiceErrorType.Validation, $"The reason must be at most {MaxReasonLength} characters.");

        var company = await FindAsync(companyId, cancellationToken);
        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Company not found.");
        if (!company.IsActive)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Company is already deactivated.");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            company.IsActive = false;
            company.DeactivatedAt = DateTime.UtcNow;
            company.DeactivatedByAdminId = adminUserId;
            await dbContext.SaveChangesAsync(cancellationToken);

            // CompanyProfile has no reason column; the audit entry is where the reason is kept.
            await auditService.LogAsync(
                AuditActions.CompanyDeactivate,
                AuditTargetTypes.Company,
                company.Id.ToString(),
                trimmed is null
                    ? $"Deactivated company '{company.CompanyName}'."
                    : $"Deactivated company '{company.CompanyName}': {trimmed}");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} deactivated company {CompanyId}.", adminUserId, companyId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ReactivateAsync(
        int companyId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        AdminWriteTransaction.EnsureNone(dbContext);
        var company = await FindAsync(companyId, cancellationToken);
        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Company not found.");
        if (company.IsActive)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Company is already active.");

        await AdminWriteTransaction.RunAsync(dbContext, async () =>
        {
            company.IsActive = true;
            company.DeactivatedAt = null;
            company.DeactivatedByAdminId = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.LogAsync(
                AuditActions.CompanyReactivate,
                AuditTargetTypes.Company,
                company.Id.ToString(),
                $"Reactivated company '{company.CompanyName}'.");
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} reactivated company {CompanyId}.", adminUserId, companyId);
        return ServiceResult.Ok();
    }

    private Task<CompanyProfile?> FindAsync(int companyId, CancellationToken cancellationToken) =>
        dbContext.CompanyProfiles.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
}
