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
}
