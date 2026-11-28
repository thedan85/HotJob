using hotjob.Models;
using hotjob.Models.Users;
using hotjob.Models.Job;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Common;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.JobTests;

public sealed class PublicJobBrowseFilterTests
{
    private static JobService CreateService(TestFixture fx) =>
        new(
            fx.Db,
            new JobPostingRepository(fx.Db),
            new EntitlementService(fx.Db, TimeProvider.System),
            new StubJobAlertService(),
            NullLogger<JobService>.Instance, TimeProvider.System);

    private static async Task<(int BackendId, int FrontendId)> SeedCategoriesAsync(TestFixture fx)
    {
        var backend = new JobCategory { Name = "Backend" };
        var frontend = new JobCategory { Name = "Frontend" };
        fx.Db.JobCategories.AddRange(backend, frontend);
        await fx.Db.SaveChangesAsync();
        return (backend.Id, frontend.Id);
    }

    private static async Task<int> SeedJobAsync(
        TestFixture fx,
        string title,
        int? categoryId,
        decimal? salaryMin,
        decimal? salaryMax,
        CompanyStatus companyStatus = CompanyStatus.Approved)
    {
        var company = new CompanyProfile
        {
            CompanyName = $"Co {Guid.NewGuid():N}",
            Status = companyStatus
        };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = title,
            Description = "Build things",
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            JobType = JobType.FullTime,
            Status = JobStatus.Published
        };
        if (categoryId is > 0)
        {
            job.CategoryMappings = new List<JobCategoryMapping> { new() { JobCategoryId = categoryId.Value } };
        }

        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task Browse_FiltersByCategory()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (backendId, frontendId) = await SeedCategoriesAsync(fx);
        await SeedJobAsync(fx, "Backend role", backendId, 1000, 2000);
        await SeedJobAsync(fx, "Frontend role", frontendId, 1000, 2000);

        var result = await CreateService(fx).BrowsePublishedAsync(null, null, null, null, null, backendId, null, null, 1, 20);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Backend role", item.Title);
        Assert.Equal(1, result.Data.TotalCount);
    }

    [Fact]
    public async Task Browse_FiltersBySalaryRange_OverlappingJobsOnly()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (backendId, _) = await SeedCategoriesAsync(fx);
        var cheapId = await SeedJobAsync(fx, "Cheap role", backendId, 1000, 2000);
        var priceyId = await SeedJobAsync(fx, "Pricey role", backendId, 4000, 6000);

        var service = CreateService(fx);

        var minFilter = await service.BrowsePublishedAsync(null, null, null, null, null, null, 1500m, null, 1, 20);
        Assert.True(minFilter.Success);
        Assert.Equal(2, minFilter.Data!.TotalCount);

        var maxFilter = await service.BrowsePublishedAsync(null, null, null, null, null, null, null, 3000m, 1, 20);
        Assert.True(maxFilter.Success);
        var item = Assert.Single(maxFilter.Data!.Items);
        Assert.Equal(cheapId, item.Id);
        Assert.DoesNotContain(maxFilter.Data.Items, job => job.Id == priceyId);

        var bandFilter = await service.BrowsePublishedAsync(null, null, null, null, null, null, 5000m, 6500m, 1, 20);
        Assert.Equal(priceyId, Assert.Single(bandFilter.Data!.Items).Id);
    }

    [Fact]
    public async Task Browse_SalaryMinAboveMax_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).BrowsePublishedAsync(
            null, null, null, null, null, null, 5000m, 1000m, 1, 20);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task Browse_ExcludesJobsFromNonApprovedCompanies(CompanyStatus companyStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedJobAsync(fx, "Open role", null, null, null);
        await SeedJobAsync(fx, "Hidden role", null, null, null, companyStatus);

        var result = await CreateService(fx).BrowsePublishedAsync(
            null, null, null, null, null, null, null, null, 1, 20);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Open role", item.Title);
        Assert.Equal(1, result.Data.TotalCount);
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task Browse_CompanyIdFilter_ExcludesNonApprovedCompany(CompanyStatus companyStatus)
    {
        await using var fx = await TestFixture.CreateAsync();
        var jobId = await SeedJobAsync(fx, "Hidden role", null, null, null, companyStatus);
        var companyId = fx.Db.JobPostings.Single(job => job.Id == jobId).CompanyProfileId;

        var result = await CreateService(fx).BrowsePublishedAsync(
            null, null, null, companyId, null, null, null, null, 1, 20);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.TotalCount);
    }

    [Fact]
    public async Task GetCategoriesAsync_ReturnsOrderedCategories()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (backendId, frontendId) = await SeedCategoriesAsync(fx);
        fx.Db.JobCategories.Add(new JobCategory { Name = "AI" });
        await fx.Db.SaveChangesAsync();

        var result = await CreateService(fx).GetCategoriesAsync();

        Assert.True(result.Success);
        Assert.Equal(new[] { "AI", "Backend", "Frontend" }, result.Data!.Select(category => category.Name));
        Assert.Contains(result.Data, category => category.Id == backendId);
        Assert.Contains(result.Data, category => category.Id == frontendId);
    }
}
