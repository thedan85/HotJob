using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

public sealed class CompanyAdminService(
    ApplicationDbContext dbContext,
    ILogger<CompanyAdminService> logger) : ICompanyAdminService
{
    public async Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.CompanyProfiles.AsNoTracking().AsQueryable();

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
            .OrderBy(c => c.CompanyName)
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
                c.VerifiedAt))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<CompanyListItemDto>>.Ok(new PagedResponse<CompanyListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListPendingAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.CompanyProfiles
            .AsNoTracking()
            .Where(c => c.Status == CompanyStatus.Pending);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.Like(c.CompanyName, term) ||
                (c.Industry != null && EF.Functions.Like(c.Industry, term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.CompanyName)
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
                c.VerifiedAt))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<CompanyListItemDto>>.Ok(new PagedResponse<CompanyListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

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
        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Company not found.");

        company.Status = CompanyStatus.Approved;
        company.VerifiedAt = DateTime.UtcNow;
        company.VerifiedByAdminId = adminUserId;
        company.RejectionReason = null;
        company.IsActive = true;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} approved company {CompanyId}.", adminUserId, companyId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectAsync(
        int companyId,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Company not found.");

        company.Status = CompanyStatus.Rejected;
        company.RejectionReason = reason;
        company.VerifiedAt = DateTime.UtcNow;
        company.VerifiedByAdminId = adminUserId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} rejected company {CompanyId}. Reason: {Reason}", adminUserId, companyId, reason);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeactivateAsync(
        int companyId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Company not found.");

        company.IsActive = false;
        company.DeactivatedAt = DateTime.UtcNow;
        company.DeactivatedByAdminId = adminUserId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} deactivated company {CompanyId}.", adminUserId, companyId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ReactivateAsync(
        int companyId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Company not found.");

        company.IsActive = true;
        company.DeactivatedAt = DateTime.UtcNow;
        company.DeactivatedByAdminId = adminUserId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} reactivated company {CompanyId}.", adminUserId, companyId);
        return ServiceResult.Ok();
    }
}
