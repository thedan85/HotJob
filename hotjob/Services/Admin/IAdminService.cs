using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

public interface IAdminService
{
    Task<ServiceResult<AdminDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponse<AdminUserListItemDto>>> GetUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Accounts filtered by search text (email/name), role and active flag, newest first.</summary>
    Task<ServiceResult<PagedResponse<AdminUserListItemDto>>> GetUsersAsync(
        AdminUserQuery query,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> ToggleUserActiveAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeleteJobAsync(int jobId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<AdminTrainingLogDto>>> GetTrainingLogsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<AdminSkillDto>>> GetSkillsAsync(
        string? search,
        string? category,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminSkillDto>> GetSkillAsync(
        int skillId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminSkillDto>> CreateSkillAsync(
        CreateSkillRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminSkillDto>> UpdateSkillAsync(
        int skillId,
        UpdateSkillRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteSkillAsync(
        int skillId,
        CancellationToken cancellationToken = default);
}
