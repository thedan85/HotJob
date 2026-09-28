using hotjob.DTOs.Employer;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

public interface IApplicantService
{
    Task<ServiceResult<PagedResult<ApplicantListItemResponse>>> ListAsync(
        string? userId,
        ApplicantListQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ApplicantDetailResponse>> GetDetailAsync(
        string? userId,
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ApplicantDetailResponse>> UpdateStatusAsync(
        string? userId,
        int applicationId,
        UpdateStatusRequest request,
        CancellationToken cancellationToken = default);
}
