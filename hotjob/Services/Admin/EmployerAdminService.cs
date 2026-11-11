using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

public sealed class EmployerAdminService(
    ApplicationDbContext dbContext,
    ILogger<EmployerAdminService> logger) : IEmployerAdminService
{
    public async Task<ServiceResult<PagedResponse<EmployerListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Employers
            .AsNoTracking()
            .Include(e => e.Company)
            .Include(e => e.User)
            .AsQueryable();

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
                e.DeactivatedAt))
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponse<EmployerListItemDto>>.Ok(new PagedResponse<EmployerListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<EmployerDetailDto>> GetDetailAsync(
        int employerId,
        CancellationToken cancellationToken = default)
    {
        var employer = await dbContext.Employers
            .AsNoTracking()
            .Include(e => e.Company)
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Id == employerId, cancellationToken);

        if (employer is null)
            return ServiceResult<EmployerDetailDto>.Fail(ServiceErrorType.NotFound, "Employer not found.");

        return ServiceResult<EmployerDetailDto>.Ok(new EmployerDetailDto(
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
            employer.Company?.CompanyName));
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

        if (employer is null)
            return ServiceResult<EmployerDetailDto>.Fail(ServiceErrorType.NotFound, "Employer not found.");

        return ServiceResult<EmployerDetailDto>.Ok(new EmployerDetailDto(
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
            employer.Company?.CompanyName));
    }

    public async Task<ServiceResult> ApproveAsync(
        int employerId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var employer = await dbContext.Employers
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Id == employerId, cancellationToken);

        if (employer is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Employer not found.");

        employer.MembershipStatus = MembershipStatus.Active;
        employer.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} approved employer {EmployerId}.", adminUserId, employerId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectAsync(
        int employerId,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var employer = await dbContext.Employers
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Id == employerId, cancellationToken);

        if (employer is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Employer not found.");

        employer.MembershipStatus = MembershipStatus.Rejected;
        employer.IsActive = false;
        employer.DeactivatedAt = DateTime.UtcNow;
        employer.DeactivatedByAdminId = adminUserId;
        employer.DeactivationReason = reason;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} rejected employer {EmployerId}. Reason: {Reason}", adminUserId, employerId, reason);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeactivateAsync(
        int employerId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var employer = await dbContext.Employers
            .FirstOrDefaultAsync(e => e.Id == employerId, cancellationToken);

        if (employer is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Employer not found.");

        employer.IsActive = false;
        employer.DeactivatedAt = DateTime.UtcNow;
        employer.DeactivatedByAdminId = adminUserId;
        employer.DeactivationReason = reason;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} deactivated employer {EmployerId}.", adminUserId, employerId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ReactivateAsync(
        int employerId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var employer = await dbContext.Employers
            .FirstOrDefaultAsync(e => e.Id == employerId, cancellationToken);

        if (employer is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Employer not found.");

        employer.IsActive = true;
        employer.DeactivatedAt = null;
        employer.DeactivatedByAdminId = null;
        employer.DeactivationReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} reactivated employer {EmployerId}.", adminUserId, employerId);
        return ServiceResult.Ok();
    }
}
