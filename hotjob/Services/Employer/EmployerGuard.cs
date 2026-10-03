using hotjob.Repositories.Employer;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

/// <summary>Shared employer resolution: valid login + Active membership + Approved company. Company scoping uses CompanyProfileId.</summary>
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
        if (employer is null)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Employer profile not found.");
        if (employer.MembershipStatus != global::hotjob.Models.Users.MembershipStatus.Active)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active.");
        // ponytail: Approved-company gate for ATS too — a Rejected/Pending company keeps its data but loses all employer actions.
        if (employer.Company?.Status != global::hotjob.Models.Users.CompanyStatus.Approved)
            return ServiceResult<global::hotjob.Models.Users.Employer>.Fail(ServiceErrorType.Unauthorized, "Employer membership is not active or the company is not approved.");

        return ServiceResult<global::hotjob.Models.Users.Employer>.Ok(employer);
    }
}
