using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.AdminTests;

public sealed class CompanyAdminServiceTests
{
    private static CompanyAdminService CreateService(TestFixture fx) =>
        new(fx.Db, NullLogger<CompanyAdminService>.Instance);

    // ---------- Approve ----------

    [Fact]
    public async Task Approve_FromPending_SetsVerification_AndKeepsMemberships()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Pending Co");
        var member = await AdminSeed.EmployerAsync(fx, company.Id, "hr@example.com", "HR", membership: MembershipStatus.Pending);

        var result = await CreateService(fx).ApproveAsync(company.Id, adminId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.Equal(adminId, company.VerifiedByAdminId);
        Assert.NotNull(company.VerifiedAt);
        Assert.Equal(MembershipStatus.Pending, member.MembershipStatus);
    }

    [Fact]
    public async Task Approve_FromRejected_ClearsRejectionReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Rejected Co", CompanyStatus.Rejected);
        company.RejectionReason = "Fake documents";
        await fx.Db.SaveChangesAsync();

        var result = await CreateService(fx).ApproveAsync(company.Id, adminId);

        Assert.True(result.Success);
        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.Null(company.RejectionReason);
    }

    [Fact]
    public async Task Approve_AlreadyApproved_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Approved Co", CompanyStatus.Approved);

        var result = await CreateService(fx).ApproveAsync(company.Id, adminId);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Approve_Missing_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);

        var result = await CreateService(fx).ApproveAsync(999, adminId);

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    // ---------- Reject ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_BlankReason_Validation(string? reason)
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Pending Co");

        var result = await CreateService(fx).RejectAsync(company.Id, adminId, reason);

        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Equal(CompanyStatus.Pending, company.Status);
    }

    [Fact]
    public async Task Reject_ReasonTooLong_Validation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Pending Co");

        var result = await CreateService(fx).RejectAsync(company.Id, adminId, new string('x', 501));

        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Reject_FromApproved_RevokesWithTrimmedReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Approved Co", CompanyStatus.Approved);

        var result = await CreateService(fx).RejectAsync(company.Id, adminId, "  Policy violation  ");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(CompanyStatus.Rejected, company.Status);
        Assert.Equal("Policy violation", company.RejectionReason);
        Assert.Equal(adminId, company.VerifiedByAdminId);
    }

    [Fact]
    public async Task Reject_AlreadyRejected_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Rejected Co", CompanyStatus.Rejected);

        var result = await CreateService(fx).RejectAsync(company.Id, adminId, "Again");

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    // ---------- Deactivate / Reactivate ----------

    [Fact]
    public async Task Deactivate_StoresTrimmedReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Active Co", CompanyStatus.Approved);

        var result = await CreateService(fx).DeactivateAsync(company.Id, adminId, "  Spam postings ");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(company.IsActive);
        Assert.NotNull(company.DeactivatedAt);
        Assert.Equal(adminId, company.DeactivatedByAdminId);
        Assert.Equal("Spam postings", company.DeactivationReason);
    }

    [Fact]
    public async Task Deactivate_WithoutReason_Succeeds()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Active Co", CompanyStatus.Approved);

        var result = await CreateService(fx).DeactivateAsync(company.Id, adminId);

        Assert.True(result.Success);
        Assert.False(company.IsActive);
        Assert.Null(company.DeactivationReason);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactive_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Inactive Co", isActive: false);

        var result = await CreateService(fx).DeactivateAsync(company.Id, adminId, "x");

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Reactivate_ClearsDeactivationFields()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Co", CompanyStatus.Approved);
        var service = CreateService(fx);
        Assert.True((await service.DeactivateAsync(company.Id, adminId, "Temporary")).Success);

        var result = await service.ReactivateAsync(company.Id, adminId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(company.IsActive);
        Assert.Null(company.DeactivatedAt);
        Assert.Null(company.DeactivatedByAdminId);
        Assert.Null(company.DeactivationReason);
    }

    [Fact]
    public async Task Reactivate_AlreadyActive_Conflict()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Active Co");

        var result = await CreateService(fx).ReactivateAsync(company.Id, adminId);

        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
    }

    // ---------- List / Detail ----------

    [Fact]
    public async Task List_FiltersByStatusAndActivity()
    {
        await using var fx = await TestFixture.CreateAsync();
        await AdminSeed.CompanyAsync(fx, "Approved Active", CompanyStatus.Approved);
        await AdminSeed.CompanyAsync(fx, "Approved Inactive", CompanyStatus.Approved, isActive: false);
        await AdminSeed.CompanyAsync(fx, "Pending Active");

        var service = CreateService(fx);
        var approvedActive = await service.ListAsync(status: CompanyStatus.Approved, isActive: true);
        var pending = await service.ListPendingAsync();
        var inactive = await service.ListAsync(isActive: false);

        Assert.Equal(new[] { "Approved Active" }, approvedActive.Data!.Items.Select(c => c.CompanyName));
        Assert.Equal(new[] { "Pending Active" }, pending.Data!.Items.Select(c => c.CompanyName));
        Assert.Equal(new[] { "Approved Inactive" }, inactive.Data!.Items.Select(c => c.CompanyName));
    }

    [Fact]
    public async Task List_ProjectsAggregates_ExcludingDeletedReviews()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await AdminSeed.CompanyAsync(fx, "Stats Co", CompanyStatus.Approved);
        await AdminSeed.EmployerAsync(fx, company.Id, "owner@example.com", "Owner", isOwner: true);
        await AdminSeed.EmployerAsync(fx, company.Id, "pending@example.com", "Pending", membership: MembershipStatus.Pending);
        await AdminSeed.JobAsync(fx, company.Id, JobStatus.Published);
        await AdminSeed.JobAsync(fx, company.Id, JobStatus.Draft, "Draft job");
        var c1 = await AdminSeed.CandidateAsync(fx, "c1@example.com", "Cand One");
        var c2 = await AdminSeed.CandidateAsync(fx, "c2@example.com", "Cand Two");
        var c3 = await AdminSeed.CandidateAsync(fx, "c3@example.com", "Cand Three");
        await AdminSeed.ReviewAsync(fx, company.Id, c1.Id, 5);
        await AdminSeed.ReviewAsync(fx, company.Id, c2.Id, 4);
        await AdminSeed.ReviewAsync(fx, company.Id, c3.Id, 1, isDeleted: true);
        await AdminSeed.CompanyAsync(fx, "Empty Co", CompanyStatus.Approved);

        var result = await CreateService(fx).ListAsync();

        Assert.True(result.Success);
        var stats = result.Data!.Items.Single(c => c.CompanyName == "Stats Co");
        Assert.True(stats.IsActive);
        Assert.Equal(1, stats.ActiveMemberCount);
        Assert.Equal(2, stats.JobCount);
        Assert.Equal(2, stats.ReviewCount);
        Assert.Equal(4.5, stats.AverageRating);

        var empty = result.Data.Items.Single(c => c.CompanyName == "Empty Co");
        Assert.Equal(0, empty.ReviewCount);
        Assert.Null(empty.AverageRating);
    }

    [Fact]
    public async Task GetDetail_ReturnsOwnerCountsAndDeactivationReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Detail Co", CompanyStatus.Approved);
        await AdminSeed.EmployerAsync(fx, company.Id, "owner@example.com", "Owner Name", isOwner: true);
        await AdminSeed.EmployerAsync(fx, company.Id, "member@example.com", "Member");
        await AdminSeed.EmployerAsync(fx, company.Id, "pending@example.com", "Pending", membership: MembershipStatus.Pending);
        await AdminSeed.JobAsync(fx, company.Id, JobStatus.PendingApproval, "Pending job");
        await AdminSeed.JobAsync(fx, company.Id, JobStatus.Published, "Live job");
        await AdminSeed.JobAsync(fx, company.Id, JobStatus.PendingCompanyApproval, "Member job");
        var c1 = await AdminSeed.CandidateAsync(fx, "c1@example.com", "Cand One");
        var c2 = await AdminSeed.CandidateAsync(fx, "c2@example.com", "Cand Two");
        await AdminSeed.ReviewAsync(fx, company.Id, c1.Id, 3);
        await AdminSeed.ReviewAsync(fx, company.Id, c2.Id, 5, isDeleted: true);
        var service = CreateService(fx);
        Assert.True((await service.DeactivateAsync(company.Id, adminId, "Under investigation")).Success);

        var result = await service.GetDetailAsync(company.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var detail = result.Data!;
        Assert.Equal("Under investigation", detail.DeactivationReason);
        Assert.Equal("Owner Name", detail.OwnerName);
        Assert.Equal("owner@example.com", detail.OwnerEmail);
        Assert.Equal(2, detail.ActiveMemberCount);
        Assert.Equal(1, detail.PendingMemberCount);
        Assert.Equal(3, detail.JobCount);
        Assert.Equal(1, detail.PendingApprovalJobCount);
        Assert.Equal(1, detail.PublishedJobCount);
        Assert.Equal(1, detail.ReviewCount);
        Assert.Equal(3.0, detail.AverageRating);
    }

    [Fact]
    public async Task GetDetail_Missing_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).GetDetailAsync(42);

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    // ---------- Company reviews ----------

    [Fact]
    public async Task GetCompanyReviews_ExcludesDeletedByDefault()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await AdminSeed.CompanyAsync(fx, "Reviewed Co", CompanyStatus.Approved);
        var other = await AdminSeed.CompanyAsync(fx, "Other Co", CompanyStatus.Approved);
        var c1 = await AdminSeed.CandidateAsync(fx, "c1@example.com", "Cand One");
        var c2 = await AdminSeed.CandidateAsync(fx, "c2@example.com", "Cand Two");
        var visible = await AdminSeed.ReviewAsync(fx, company.Id, c1.Id, 4, "Nice place");
        var hidden = await AdminSeed.ReviewAsync(fx, company.Id, c2.Id, 1, "Spam", isDeleted: true);
        await AdminSeed.ReviewAsync(fx, other.Id, c1.Id, 2, "Other company");
        var service = CreateService(fx);

        var defaultResult = await service.GetCompanyReviewsAsync(company.Id);
        var withDeleted = await service.GetCompanyReviewsAsync(company.Id, includeDeleted: true);

        Assert.True(defaultResult.Success);
        var item = Assert.Single(defaultResult.Data!.Items);
        Assert.Equal(visible.Id, item.Id);
        Assert.Equal("Cand One", item.CandidateName);
        Assert.Equal(1, defaultResult.Data.TotalCount);

        Assert.Equal(2, withDeleted.Data!.TotalCount);
        Assert.Contains(withDeleted.Data.Items, r => r.Id == hidden.Id && r.IsDeleted);
    }

    [Fact]
    public async Task GetCompanyReviews_MissingCompany_NotFound()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateService(fx).GetCompanyReviewsAsync(404);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }
}
