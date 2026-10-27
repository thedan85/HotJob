using hotjob.Models.Billing;

namespace hotjob.Services.Billing;

/// <summary>Effective employer plan and its active-job usage at a point in time.</summary>
public sealed record EmployerEntitlement(
    int CompanyProfileId,
    bool IsOwner,
    string PlanCode,
    string PlanName,
    int JobQuota,
    int ActiveJobCount,
    DateTime? PlanExpiresAt)
{
    /// <summary>Free slots left, never negative.</summary>
    public int RemainingSlots => Math.Max(0, JobQuota - ActiveJobCount);

    /// <summary>Whether a new or re-opened active job fits in the plan.</summary>
    public bool CanAddJob => ActiveJobCount < JobQuota;

    /// <summary>Whether the company is on the perpetual free plan.</summary>
    public bool IsFree => PlanCode == Plan.FreeCode;

    /// <summary>Whether legacy data already exceeds the plan (grandfathered, never deleted).</summary>
    public bool IsOverQuota => ActiveJobCount > JobQuota;
}

/// <summary>Reads the effective plan, quota and active-job usage for employers.</summary>
public interface IEntitlementService
{
    /// <summary>Returns the entitlement for an active member of an approved company, else null.</summary>
    Task<EmployerEntitlement?> GetForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Returns the entitlement for a company; <paramref name="isOwner"/> is echoed to the caller.</summary>
    Task<EmployerEntitlement?> GetForCompanyAsync(
        int companyProfileId,
        bool isOwner,
        CancellationToken cancellationToken = default);

    /// <summary>Paid, active employer plans in display order.</summary>
    Task<IReadOnlyList<Plan>> GetPurchasableEmployerPlansAsync(CancellationToken cancellationToken = default);
}
