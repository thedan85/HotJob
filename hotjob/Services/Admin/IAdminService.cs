using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface IAdminService
{
    Task<ServiceResult<AdminDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<AdminUserListItemDto>>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> ToggleUserActiveAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<AdminCompanyListItemDto>>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> UpdateCompanyStatusAsync(int companyId, CompanyStatus status, string? rejectionReason, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<AdminJobListItemDto>>> GetJobsAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeleteJobAsync(int jobId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<AdminSkillDto>>> GetSkillsAsync(
        string? search,
        string? category,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminSkillDto>> GetSkillAsync(
        int skillId,
        CancellationToken cancellationToken = default);
}
