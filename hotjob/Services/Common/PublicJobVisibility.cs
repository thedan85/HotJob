using System.Linq.Expressions;
using hotjob.Models.Job;
using hotjob.Models.Users;

namespace hotjob.Services.Common;

/// <summary>
/// Opt-in, non-global visibility predicate for candidate/public job queries.
/// Covers company approval only; publication and expiry rules stay with each caller.
/// </summary>
public static class PublicJobVisibility
{
    public static readonly Expression<Func<JobPosting, bool>> CompanyApproved =
        job => job.Company.Status == CompanyStatus.Approved;

    public static IQueryable<JobPosting> WhereCompanyApproved(this IQueryable<JobPosting> query) =>
        query.Where(CompanyApproved);
}
