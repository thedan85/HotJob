using hotjob.Repositories.Employer;
using hotjob.Services.Common;
using hotjob.Models.Users;

namespace hotjob.Services.Employer;

/// <summary>Shared employer resolution: valid login + Active membership + not deactivated + Approved company. Company scoping uses CompanyProfileId.</summary>
internal static class EmployerGuard
{
    public static async Task<ServiceResult<global::hotjob.Models.Users.Employer>> RequireActiveAsync(
        IApplicantRepository repo,
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Authentication required.");

        var employer = await repo.GetEmployerByUserIdAsync(userId, cancellationToken);
        return Evaluate(employer, requireApprovedCompany: true);
    }

    /// <summary>
    /// Pure access rules for a loaded employer (Company must be loaded). Always requires an active employer
    /// row, Active membership and an active company; optionally also an Approved company
    /// (a Pending/Rejected company keeps its data but loses all write actions).
    /// </summary>
    public static ServiceResult<global::hotjob.Models.Users.Employer> Evaluate(
        global::hotjob.Models.Users.Employer? employer,
        bool requireApprovedCompany)
    {
        if (employer is null)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Employer profile not found.");
        if (!employer.IsActive)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Employer account is deactivated.");
        if (employer.MembershipStatus != MembershipStatus.Active)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active.");
        if (employer.Company is null || !employer.Company.IsActive)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Company is deactivated.");
        // ponytail: Approved-company gate for ATS too — a Rejected/Pending company keeps its data but loses all employer actions.
        if (requireApprovedCompany && employer.Company.Status != CompanyStatus.Approved)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");

        return ServiceResult<global::hotjob.Models.Users.Employer>.Ok(employer);
    }
}
