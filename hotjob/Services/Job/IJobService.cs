using hotjob.DTOs.Job;
using hotjob.Models.Job;
using hotjob.Services.Common;

namespace hotjob.Services.Job;

public interface IJobService
{
    Task<ServiceResult<JobDetailResponse>> CreateAsync(
        string userId,
        CreateJobRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<JobDetailResponse>> UpdateAsync(
        string userId,
        int id,
        UpdateJobRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<JobDeleteResponse>> DeleteOrCloseAsync(
        string userId,
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<JobDetailResponse>> AssignCategoriesAsync(
        string userId,
        int id,
        AssignCategoriesRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<JobDetailResponse>> AssignSkillsAsync(
        string userId,
        int id,
        AssignSkillsRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<JobListItemResponse>>> ListAsync(
        string userId,
        JobStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<JobDetailResponse>> GetAsync(
        string userId,
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<PublicJobListItemResponse>>> BrowsePublishedAsync(
        string? keyword,
        string? city,
        string? skill,
        int? companyId,
        JobType? jobType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<SkillMatchJobListItemDto>>> GetJobsMatchingResumeAsync(
        string userId,
        int resumeId,
        string? city,
        JobType? jobType,
        int minSkillMatches,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<JobDetailResponse>> GetPublishedAsync(
        int id,
        CancellationToken cancellationToken = default);
}
