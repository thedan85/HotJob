using hotjob.DTOs.Company;
using hotjob.DTOs.Job;
using hotjob.Services.Common;

namespace hotjob.Services.Company;

public interface ICompanyService
{
    Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
        CompanyListQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<CompanyFilterOptionsDto>> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<CompanyDetailDto>> GetDetailAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResponse<CompanyReviewDto>>> GetReviewsAsync(
        int companyId,
        int page,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<CompanyReviewDto>> CreateReviewAsync(
        string? userId,
        int companyId,
        CreateReviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The signed-in candidate's own reviews, newest first, optionally for one company.
    /// Includes reviews a moderator hid, flagged, so the author knows why it is not public.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<MyReviewDto>>> GetMyReviewsAsync(
        string? userId,
        int? companyId = null,
        CancellationToken cancellationToken = default);
}
