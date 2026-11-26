using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.Models.Admin;
using hotjob.Models.Application;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.AdminTests;

/// <summary>Admin phase 1 (A1.1–A1.3): account search/filter, account detail and password reset.</summary>
public sealed class AdminUserManagementTests
{
    private const string AdminEmail = "moderator@example.com";

    private static AdminUserService CreateUserService(TestFixture fx) =>
        new(fx.Db, fx.UserManager, fx.CreateAuditService(), NullLogger<AdminUserService>.Instance);

    private static async Task<string> SignInAdminAsync(TestFixture fx)
    {
        var admin = await fx.SeedUserAsync(AdminEmail, role: AppRoles.Admin);
        fx.SignInAdminOnCircuit(admin.Id, AdminEmail);
        return admin.Id;
    }

    // ------------------------------------------------------------------ A1.1 search / filter

    private static async Task SeedDirectoryAsync(TestFixture fx)
    {
        await fx.SeedUserAsync(AdminEmail, role: AppRoles.Admin);
        var (alice, _) = await MatchTestData.CreateCandidateAsync(fx, "alice@example.com");
        alice.FullName = "Alice Nguyen";
        await MatchTestData.CreateCandidateAsync(fx, "bob@example.com");
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var hr = await MatchTestData.CreateEmployerAsync(fx, company.Id, "hr@acme.test");
        hr.FullName = "Carol Tran";
        var disabled = await fx.Db.Users.SingleAsync(u => u.Email == "bob@example.com");
        disabled.IsActive = false;
        await fx.Db.SaveChangesAsync();
    }

    private static async Task<string[]> EmailsAsync(TestFixture fx, AdminUserQuery query)
    {
        var result = await fx.CreateAdminService().GetUsersAsync(query);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return result.Data!.Items.Select(u => u.Email).OrderBy(e => e).ToArray();
    }

    [Fact]
    public async Task GetUsers_FiltersByRole()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedDirectoryAsync(fx);

        Assert.Equal(["alice@example.com", "bob@example.com"], await EmailsAsync(fx, new AdminUserQuery { Role = AppRoles.Candidate }));
        Assert.Equal(["hr@acme.test"], await EmailsAsync(fx, new AdminUserQuery { Role = AppRoles.Employer }));
        Assert.Equal([AdminEmail], await EmailsAsync(fx, new AdminUserQuery { Role = AppRoles.Admin }));
        Assert.Equal(4, (await EmailsAsync(fx, new AdminUserQuery())).Length);
    }

    [Fact]
    public async Task GetUsers_SearchesEmailAndName_CaseInsensitive()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedDirectoryAsync(fx);

        Assert.Equal(["alice@example.com"], await EmailsAsync(fx, new AdminUserQuery { Search = "  ALICE " }));
        Assert.Equal(["alice@example.com"], await EmailsAsync(fx, new AdminUserQuery { Search = "nguyen" }));
        Assert.Equal(["hr@acme.test"], await EmailsAsync(fx, new AdminUserQuery { Search = "carol" }));
        Assert.Empty(await EmailsAsync(fx, new AdminUserQuery { Search = "%" }));
    }

    [Fact]
    public async Task GetUsers_CombinesFilters_WithPaging()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SeedDirectoryAsync(fx);
        var service = fx.CreateAdminService();

        Assert.Equal(["alice@example.com"], await EmailsAsync(fx, new AdminUserQuery { Role = AppRoles.Candidate, IsActive = true }));
        Assert.Equal(["bob@example.com"], await EmailsAsync(fx, new AdminUserQuery { Role = AppRoles.Candidate, IsActive = false }));

        var page = await service.GetUsersAsync(new AdminUserQuery { Search = "example.com", Page = 2, PageSize = 2 });
        Assert.Equal(3, page.Data!.TotalCount);
        Assert.Single(page.Data.Items);

        var badRole = await service.GetUsersAsync(new AdminUserQuery { Role = "Root" });
        Assert.Equal(ServiceErrorType.Validation, badRole.ErrorType);
    }

    // ------------------------------------------------------------------ A1.2 detail

    [Fact]
    public async Task Detail_Candidate_ShowsResumesApplicationsAndAudit()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SignInAdminAsync(fx);
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "alice@example.com", ("C#", 2));
        candidate.PremiumUntil = DateTime.UtcNow.AddDays(10);
        candidate.IsDiscoverable = true;
        await fx.Db.SaveChangesAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, [], title: "Backend dev");
        await MatchTestData.CreateApplicationAsync(fx, job, resume, candidate, ApplicationStatus.Interviewing);
        await fx.CreateAdminService().ToggleUserActiveAsync(candidate.UserId);

        var result = await CreateUserService(fx).GetDetailAsync(candidate.UserId);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var detail = result.Data!;
        Assert.Equal("alice@example.com", detail.Email);
        Assert.False(detail.IsActive);
        Assert.Equal([AppRoles.Candidate], detail.Roles);
        Assert.Null(detail.Employer);
        var profile = Assert.IsType<AdminCandidateSummaryDto>(detail.Candidate);
        Assert.True(profile.IsPremium);
        Assert.True(profile.IsDiscoverable);
        var cv = Assert.Single(profile.Resumes);
        Assert.True(cv.IsPrimary);
        Assert.False(cv.HasFile);
        Assert.Equal(1, profile.ApplicationCount);
        var application = Assert.Single(profile.RecentApplications);
        Assert.Equal("Backend dev", application.JobTitle);
        Assert.Equal(company.CompanyName, application.CompanyName);
        Assert.Equal(ApplicationStatus.Interviewing, application.Status);
        Assert.Equal(AuditActions.UserLock, Assert.Single(detail.RecentAudit).Action);
    }

    [Fact]
    public async Task Detail_EmployerOnly_ShowsMembership()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var employer = await MatchTestData.CreateEmployerAsync(fx, company.Id, "hr@acme.test");
        await MatchTestData.CreateJobAsync(fx, company.Id, []);
        var job = await fx.Db.JobPostings.SingleAsync();
        job.CreatedByEmployerId = employer.Id;
        await fx.Db.SaveChangesAsync();

        var detail = (await CreateUserService(fx).GetDetailAsync(employer.UserId)).Data!;

        Assert.Null(detail.Candidate);
        var membership = Assert.IsType<AdminEmployerSummaryDto>(detail.Employer);
        Assert.Equal(company.CompanyName, membership.CompanyName);
        Assert.Equal(MembershipStatus.Active, membership.MembershipStatus);
        Assert.True(membership.IsOwner);
        Assert.Equal(1, membership.JobsCreated);
    }

    [Fact]
    public async Task Detail_CandidateAndEmployer_ShowsBoth()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (candidate, _) = await MatchTestData.CreateCandidateAsync(fx, "both@example.com");
        var company = await MatchTestData.CreateCompanyAsync(fx);
        fx.Db.Employers.Add(new Employer
        {
            UserId = candidate.UserId,
            CompanyProfileId = company.Id,
            FullName = "Both",
            MembershipStatus = MembershipStatus.Pending
        });
        await fx.Db.SaveChangesAsync();

        var detail = (await CreateUserService(fx).GetDetailAsync(candidate.UserId)).Data!;

        Assert.NotNull(detail.Candidate);
        Assert.Equal(MembershipStatus.Pending, detail.Employer!.MembershipStatus);
    }

    [Fact]
    public async Task Detail_UnknownUser_IsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();

        var result = await CreateUserService(fx).GetDetailAsync("missing");

        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    // ------------------------------------------------------------------ A1.3 password reset

    [Fact]
    public async Task ResetPassword_SetsTemporaryPassword_RevokesSessions_AndAuditsWithoutSecret()
    {
        await using var fx = await TestFixture.CreateAsync();
        await SignInAdminAsync(fx);
        var target = await fx.SeedUserAsync("target@example.com", "Str0ng!Pass", AppRoles.Candidate);
        fx.Db.RefreshTokens.Add(new RefreshToken { TokenHash = "hash-1", UserId = target.Id, ExpiresAtUtc = DateTime.UtcNow.AddDays(7) });
        await fx.Db.SaveChangesAsync();

        var result = await CreateUserService(fx).ResetPasswordAsync(target.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var temporary = result.Data!.TemporaryPassword;
        fx.Db.ChangeTracker.Clear();
        var reloaded = await fx.UserManager.FindByIdAsync(target.Id);
        Assert.False(await fx.UserManager.CheckPasswordAsync(reloaded!, "Str0ng!Pass"));
        Assert.True(await fx.UserManager.CheckPasswordAsync(reloaded!, temporary));

        var token = await fx.Db.RefreshTokens.AsNoTracking().SingleAsync();
        Assert.NotNull(token.RevokedAtUtc);
        Assert.Equal("PasswordReset", token.RevokedReason);

        var log = await fx.Db.AdminAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(AuditActions.UserResetPassword, log.Action);
        Assert.Equal(target.Id, log.TargetId);
        Assert.DoesNotContain(temporary, log.Detail ?? string.Empty);
    }

    [Fact]
    public async Task ResetPassword_RefusesSelfOtherAdminsAndUnknownUsers()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await SignInAdminAsync(fx);
        var otherAdmin = await fx.SeedUserAsync("admin2@example.com", "Str0ng!Pass", AppRoles.Admin);
        var service = CreateUserService(fx);

        Assert.Equal(ServiceErrorType.Conflict, (await service.ResetPasswordAsync(adminId)).ErrorType);
        Assert.Equal(ServiceErrorType.Conflict, (await service.ResetPasswordAsync(otherAdmin.Id)).ErrorType);
        Assert.Equal(ServiceErrorType.NotFound, (await service.ResetPasswordAsync("missing")).ErrorType);
        Assert.Empty(await fx.Db.AdminAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ResetPassword_WithoutSignedInAdmin_Throws()
    {
        await using var fx = await TestFixture.CreateAsync();
        var target = await fx.SeedUserAsync("target@example.com", "Str0ng!Pass", AppRoles.Candidate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateUserService(fx).ResetPasswordAsync(target.Id));

        fx.Db.ChangeTracker.Clear();
        Assert.True(await fx.UserManager.CheckPasswordAsync((await fx.UserManager.FindByIdAsync(target.Id))!, "Str0ng!Pass"));
    }

    [Fact]
    public void TemporaryPassword_AlwaysMeetsPolicy()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = AdminUserService.GenerateTemporaryPassword();
            Assert.Equal(16, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        }
    }
}
