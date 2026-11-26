using System.Linq.Expressions;

namespace hotjob.Models.Users;

/// <summary>
/// Single definition of "a company the public may see and review": approved and not deactivated
/// by an admin. Mirrors the company half of <see cref="Job.JobVisibility.OpenToCandidates"/>.
/// </summary>
public static class CompanyVisibility
{
    public static Expression<Func<CompanyProfile, bool>> IsPublic { get; } =
        company => company.Status == CompanyStatus.Approved && company.IsActive;
}
