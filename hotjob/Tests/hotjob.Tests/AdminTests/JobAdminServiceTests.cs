using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.AdminTests;

public sealed class JobAdminServiceTests
{
    private static JobAdminService CreateService(TestFixture fx) =>
        new(fx.Db, NullLogger<JobAdminService>.Instance);

    [Fact]
    public async Task Approve_PendingCompanyApproval_ConflictMentionsOwner()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var job = await AdminSeed.JobAsync(fx, company.Id, JobStatus.PendingCompanyApproval);

        var result = await CreateService(fx).ApproveAsync(job.Id, adminId);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("company owner", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JobStatus.PendingCompanyApproval, job.Status);
        Assert.Null(job.ReviewedByAdminId);
    }

    [Fact]
    public async Task Reject_PendingCompanyApproval_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var job = await AdminSeed.JobAsync(fx, company.Id, JobStatus.PendingCompanyApproval);

        var result = await CreateService(fx).RejectAsync(job.Id, adminId, "No");

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Rejected)]
    [InlineData(JobStatus.Published)]
    public async Task Approve_NotPendingApproval_Conflict(JobStatus status)
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var job = await AdminSeed.JobAsync(fx, company.Id, status);

        var result = await CreateService(fx).ApproveAsync(job.Id, adminId);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Approve_PendingApproval_Succeeds()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var job = await AdminSeed.JobAsync(fx, company.Id, JobStatus.PendingApproval);

        var result = await CreateService(fx).ApproveAsync(job.Id, adminId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(JobStatus.Approved, job.Status);
        Assert.Equal(adminId, job.ReviewedByAdminId);
        Assert.NotNull(job.ReviewedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Reject_BlankReason_Validation(string? reason)
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var job = await AdminSeed.JobAsync(fx, company.Id, JobStatus.PendingApproval);

        var result = await CreateService(fx).RejectAsync(job.Id, adminId, reason);

        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Equal(JobStatus.PendingApproval, job.Status);
    }

    [Fact]
    public async Task Reject_ThenDetail_MapsRejectedAtAndReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var job = await AdminSeed.JobAsync(fx, company.Id, JobStatus.PendingApproval);
        var service = CreateService(fx);

        var reject = await service.RejectAsync(job.Id, adminId, "  Missing salary range ");
        var detail = await service.GetDetailAsync(job.Id);

        Assert.True(reject.Success, string.Join("; ", reject.Errors));
        Assert.True(detail.Success);
        Assert.Equal(nameof(JobStatus.Rejected), detail.Data!.Status);
        Assert.NotNull(detail.Data.RejectedAt);
        Assert.Null(detail.Data.ApprovedAt);
        Assert.Equal("Missing salary range", detail.Data.RejectionReason);
        Assert.Equal(adminId, detail.Data.ReviewedByAdminId);
    }

    [Fact]
    public async Task Approve_ThenDetail_MapsApprovedAt()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var job = await AdminSeed.JobAsync(fx, company.Id, JobStatus.PendingApproval);
        var service = CreateService(fx);

        Assert.True((await service.ApproveAsync(job.Id, adminId)).Success);
        var detail = await service.GetDetailAsync(job.Id);

        Assert.NotNull(detail.Data!.ApprovedAt);
        Assert.Null(detail.Data.RejectedAt);
        Assert.Null(detail.Data.RejectionReason);
    }
}
