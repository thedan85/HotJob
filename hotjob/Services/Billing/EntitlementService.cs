using hotjob.Data;
using hotjob.Models.Billing;
using hotjob.Models.Job;
using hotjob.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Billing;

/// <summary>
/// Resolves the effective plan: a paid plan while it has not expired, otherwise FREE.
/// Usage counts Draft and Published jobs only, so closing a job frees a slot (contract D7).
/// </summary>
public sealed class EntitlementService(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : IEntitlementService
{
    public async Task<EmployerEntitlement?> GetForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var employer = await dbContext.Employers
            .AsNoTracking()
            .Include(member => member.Company)
            .FirstOrDefaultAsync(
                member => member.UserId == userId
                    && member.MembershipStatus == MembershipStatus.Active
                    && member.Company.Status == CompanyStatus.Approved,
                cancellationToken);

        return employer is null
            ? null
            : await GetForCompanyAsync(employer.CompanyProfileId, employer.IsOwner, cancellationToken);
    }

    public async Task<EmployerEntitlement?> GetForCompanyAsync(
        int companyProfileId,
        bool isOwner,
        CancellationToken cancellationToken = default)
    {
        var company = await dbContext.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Id == companyProfileId, cancellationToken);
        if (company is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var paidPlanActive = company.PlanId is not null
            && (company.PlanExpiresAt is null || company.PlanExpiresAt > now);

        Plan? plan = null;
        if (paidPlanActive)
        {
            plan = await dbContext.Plans
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == company.PlanId, cancellationToken);
        }

        plan ??= await dbContext.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == Plan.FreeCode, cancellationToken);

        // The catalogue is seeded at startup; this keeps quota enforcement working if a
        // scratch database has not been seeded yet.
        plan ??= new Plan { Code = Plan.FreeCode, Name = "Free", JobQuota = 1, DurationDays = 0 };

        var activeJobs = await dbContext.JobPostings
            .AsNoTracking()
            .CountAsync(
                job => job.CompanyProfileId == companyProfileId
                    && (job.Status == JobStatus.Draft || job.Status == JobStatus.Published),
                cancellationToken);

        return new EmployerEntitlement(
            companyProfileId,
            isOwner,
            plan.Code,
            plan.Name,
            plan.JobQuota,
            activeJobs,
            plan.Code == Plan.FreeCode ? null : company.PlanExpiresAt);
    }

    public async Task<IReadOnlyList<Plan>> GetPurchasableEmployerPlansAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Plans
            .AsNoTracking()
            .Where(plan => plan.Audience == PlanAudience.Employer && plan.IsActive && plan.Price > 0)
            .OrderBy(plan => plan.SortOrder)
            .ThenBy(plan => plan.Price)
            .ToListAsync(cancellationToken);
}
