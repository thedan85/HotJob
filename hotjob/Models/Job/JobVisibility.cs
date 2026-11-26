using System.Linq.Expressions;
using hotjob.Models.Users;

namespace hotjob.Models.Job;

/// <summary>
/// Single definition of "a job candidates and guests may see and apply to", shared by search,
/// job detail, recommendations, saving and applying so they cannot drift apart.
/// </summary>
public static class JobVisibility
{
    /// <summary>
    /// Published, not expired, and owned by a company that is approved and not deactivated by an
    /// admin. Deactivating a company therefore hides its jobs everywhere at once, and reactivating
    /// it brings them back unchanged.
    /// </summary>
    public static Expression<Func<JobPosting, bool>> OpenToCandidates(DateTime nowUtc) =>
        job => job.Status == JobStatus.Published
            && (job.ExpiryDate == null || job.ExpiryDate > nowUtc)
            && job.Company.Status == CompanyStatus.Approved
            && job.Company.IsActive;
}
