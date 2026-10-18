using hotjob.DTOs.Company;
using hotjob.DTOs.Job;
using hotjob.Repositories.Company;
using hotjob.Services.Common;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Company;

public sealed class CompanyService(
    ICompanyRepository repository,
    ILogger<CompanyService> logger) : ICompanyService
{
    public const int PageSize = 20;
    public const int ReviewPageSize = 5;

    public async Task<ServiceResult<PagedResponse<CompanyListItemDto>>> ListAsync(
        CompanyListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
            return ServiceResult<PagedResponse<CompanyListItemDto>>.Fail(ServiceErrorType.Validation, "Query is required.");

        var page = Math.Max(query.Page, 1);
        var (items, total) = await repository.ListApprovedAsync(
            query.Search, query.Industries, query.Locations, page, PageSize, cancellationToken);

        logger.LogDebug("Listed {Count} companies (page {Page}, total {Total}).", items.Count, page, total);

        return ServiceResult<PagedResponse<CompanyListItemDto>>.Ok(new PagedResponse<CompanyListItemDto>
        {
            Items = items.Select(company => new CompanyListItemDto(
                company.Id,
                company.CompanyName,
                company.Industry,
                company.Location,
                company.Scale,
                company.LogoUrl,
                company.Description)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = PageSize
        });
    }

    public async Task<ServiceResult<CompanyFilterOptionsDto>> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var (industries, locations) = await repository.ListFilterOptionsAsync(cancellationToken);
        return ServiceResult<CompanyFilterOptionsDto>.Ok(new CompanyFilterOptionsDto(industries, locations));
    }

    public async Task<ServiceResult<CompanyDetailDto>> GetDetailAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
            return ServiceResult<CompanyDetailDto>.Fail(ServiceErrorType.NotFound, "Company not found.");

        var company = await repository.GetApprovedByIdAsync(id, cancellationToken);
        if (company is null)
            return ServiceResult<CompanyDetailDto>.Fail(ServiceErrorType.NotFound, "Company not found.");

        var ownerName = await repository.GetOwnerNameAsync(id, cancellationToken);

        return ServiceResult<CompanyDetailDto>.Ok(new CompanyDetailDto(
            company.Id,
            company.CompanyName,
            company.Industry,
            company.Location,
            company.Scale,
            company.LogoUrl,
            company.Description,
            company.Website,
            ownerName));
    }

    public async Task<ServiceResult<PagedResponse<CompanyReviewDto>>> GetReviewsAsync(
        int companyId,
        int page,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        var (items, total) = await repository.ListReviewsAsync(companyId, page, ReviewPageSize, cancellationToken);

        return ServiceResult<PagedResponse<CompanyReviewDto>>.Ok(new PagedResponse<CompanyReviewDto>
        {
            Items = items.Select(review => new CompanyReviewDto(
                review.Id,
                review.RatingScore,
                review.ReviewText,
                review.Candidate?.FullName ?? "Anonymous",
                review.CreatedAt)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = ReviewPageSize
        });
    }
}
