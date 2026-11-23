using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Common;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Admin;

/// <summary>
/// Admin moderation of company profiles. Verification (<see cref="CompanyStatus"/>) and
/// activity (<see cref="CompanyProfile.IsActive"/>) are independent axes; neither touches employer memberships.
/// </summary>
public sealed class CompanyAdminService(
    ApplicationDbContext dbContext,
    ILogger<CompanyAdminService> logger) : ICompanyAdminService
{
    private const string NotFoundMessage = "Company not found.";

    public async Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        CompanyStatus? status = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.CompanyProfiles.AsNoTracking();

        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);

        if (isActive.HasValue)
            query = query.Where(c => c.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.Like(c.CompanyName, term) ||
                (c.Industry != null && EF.Functions.Like(c.Industry, term)) ||
                (c.Location != null && EF.Functions.Like(c.Location, term)));
        }

        var total = await query.CountAsync(cancellationToken);

        // Aggregates are correlated subqueries in a single SQL statement (no N+1).
        var rows = await query
            .OrderBy(c => c.CompanyName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.CompanyName,
                c.Industry,
                c.Location,
                c.LogoUrl,
                c.Status,
                c.RejectionReason,
                c.VerifiedAt,
                c.IsActive,
                ActiveMemberCount = c.Employers.Count(e => e.MembershipStatus == MembershipStatus.Active && e.IsActive),
                JobCount = c.JobPostings.Count(),
                ReviewCount = c.Reviews.Count(r => !r.IsDeleted),
                AverageRating = c.Reviews.Where(r => !r.IsDeleted).Average(r => (double?)r.RatingScore)
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new CompanyListItemDto(
                r.Id,
                r.CompanyName,
                r.Industry,
                r.Location,
                r.LogoUrl,
                r.Status.ToString(),
                r.RejectionReason,
                r.VerifiedAt,
                r.IsActive,
                r.ActiveMemberCount,
                r.JobCount,
                r.ReviewCount,
                RoundRating(r.AverageRating)))
            .ToList();

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
        ListAsync(page, pageSize, search, CompanyStatus.Pending, null, cancellationToken);

    public async Task<ServiceResult<CompanyDetailDto>> GetDetailAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var row = await dbContext.CompanyProfiles
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new
            {
                Company = c,
                OwnerName = c.Employers
                    .Where(e => e.IsOwner)
                    .OrderBy(e => e.JoinedAt)
                    .Select(e => e.FullName)
                    .FirstOrDefault(),
                OwnerEmail = c.Employers
                    .Where(e => e.IsOwner)
                    .OrderBy(e => e.JoinedAt)
                    .Select(e => e.User.Email)
                    .FirstOrDefault(),
                ActiveMemberCount = c.Employers.Count(e => e.MembershipStatus == MembershipStatus.Active && e.IsActive),
                PendingMemberCount = c.Employers.Count(e => e.MembershipStatus == MembershipStatus.Pending),
                JobCount = c.JobPostings.Count(),
                PendingApprovalJobCount = c.JobPostings.Count(j => j.Status == JobStatus.PendingApproval),
                PublishedJobCount = c.JobPostings.Count(j => j.Status == JobStatus.Published),
                ReviewCount = c.Reviews.Count(r => !r.IsDeleted),
                AverageRating = c.Reviews.Where(r => !r.IsDeleted).Average(r => (double?)r.RatingScore)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return ServiceResult<CompanyDetailDto>.Fail(ServiceErrorType.NotFound, NotFoundMessage);

        var company = row.Company;
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
            company.DeactivatedByAdminId,
            company.DeactivationReason,
            row.OwnerName,
            row.OwnerEmail,
            row.ActiveMemberCount,
            row.PendingMemberCount,
            row.JobCount,
            row.PendingApprovalJobCount,
            row.PublishedJobCount,
            row.ReviewCount,
            RoundRating(row.AverageRating)));
    }

    public async Task<ServiceResult<PagedResponse<ReviewListItemDto>>> GetCompanyReviewsAsync(
        int companyId,
        int page = 1,
        int pageSize = 20,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.CompanyProfiles
            .AnyAsync(c => c.Id == companyId, cancellationToken);

        if (!exists)
            return ServiceResult<PagedResponse<ReviewListItemDto>>.Fail(ServiceErrorType.NotFound, NotFoundMessage);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Reviews
            .AsNoTracking()
            .Where(r => r.CompanyProfileId == companyId);

        if (!includeDeleted)
            query = query.Where(r => !r.IsDeleted);

        return ServiceResult<PagedResponse<ReviewListItemDto>>.Ok(
            await ReviewAdminService.ToPageAsync(query, page, pageSize, cancellationToken));
    }

    public async Task<ServiceResult> ApproveAsync(
        int companyId,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, NotFoundMessage);

        if (company.Status == CompanyStatus.Approved)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Company is already approved.");

        company.Status = CompanyStatus.Approved;
        company.VerifiedAt = DateTime.UtcNow;
        company.VerifiedByAdminId = adminUserId;
        company.RejectionReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} approved company {CompanyId}.", adminUserId, companyId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectAsync(
        int companyId,
        string adminUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var normalized = ModerationReason.Normalize(reason);
        if (ModerationReason.ValidateRequired(normalized) is { } invalid)
            return invalid;

        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, NotFoundMessage);

        if (company.Status == CompanyStatus.Rejected)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Company is already rejected.");

        var previousStatus = company.Status;
        company.Status = CompanyStatus.Rejected;
        company.RejectionReason = normalized;
        company.VerifiedAt = DateTime.UtcNow;
        company.VerifiedByAdminId = adminUserId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Admin {AdminId} rejected company {CompanyId} (was {PreviousStatus}). Reason: {Reason}",
            adminUserId, companyId, previousStatus, normalized);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeactivateAsync(
        int companyId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = ModerationReason.Normalize(reason);
        if (ModerationReason.ValidateOptional(normalized) is { } invalid)
            return invalid;

        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, NotFoundMessage);

        if (!company.IsActive)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Company is already deactivated.");

        company.IsActive = false;
        company.DeactivatedAt = DateTime.UtcNow;
        company.DeactivatedByAdminId = adminUserId;
        company.DeactivationReason = normalized;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Admin {AdminId} deactivated company {CompanyId}. Reason: {Reason}", adminUserId, companyId, normalized);
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
            return ServiceResult.Fail(ServiceErrorType.NotFound, NotFoundMessage);

        if (company.IsActive)
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Company is already active.");

        company.IsActive = true;
        company.DeactivatedAt = null;
        company.DeactivatedByAdminId = null;
        company.DeactivationReason = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {AdminId} reactivated company {CompanyId}.", adminUserId, companyId);
        return ServiceResult.Ok();
    }

    private static double? RoundRating(double? average) =>
        average.HasValue ? Math.Round(average.Value, 2) : null;
}
