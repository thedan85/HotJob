using hotjob.DTOs.Admin;
using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Admin;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.AdminTests;

public sealed class AdminAuditHooksTests
{
    [Theory]
    [InlineData("company")]
    [InlineData("employer")]
    [InlineData("jobApprove")]
    [InlineData("review")]
    [InlineData("user")]
    [InlineData("job")]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task AdminWrite_ExistingTransaction_ThrowsBeforeChanges(string operation)
    {
        await using var fx = await TestFixture.CreateAsync();
        await using var transaction = await fx.Db.Database.BeginTransactionAsync();
        var service = fx.CreateAdminService();
        Func<Task> invoke = operation switch
        {
            "company" => () => fx.CreateCompanyAdminService().ApproveAsync(1, "admin"),
            "employer" => () => fx.CreateEmployerAdminService().ApproveAsync(1, "admin"),
            "jobApprove" => () => fx.CreateJobAdminService().ApproveAsync(1, "admin"),
            "review" => () => fx.CreateReviewAdminService().DeleteAsync(1, "admin"),
            "user" => () => service.ToggleUserActiveAsync("missing"),
            "job" => () => service.DeleteJobAsync(1),
            "create" => () => service.CreateSkillAsync(new CreateSkillRequest { Name = "Rust" }),
            "update" => () => service.UpdateSkillAsync(1, new UpdateSkillRequest { Name = "Rust" }),
            "delete" => () => service.DeleteSkillAsync(1),
            _ => throw new InvalidOperationException()
        };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(invoke);
        Assert.Contains("transaction", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(transaction, fx.Db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("lock")]
    [InlineData("unlock")]
    [InlineData("job")]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task AuditInsertFailure_RollsBackAndSameContextCanRetry(string operation)
    {
        await using var fx = await TestFixture.CreateAsync();
        var admin = await fx.SeedUserAsync("admin@example.com", role: AppRoles.Admin);
        var target = await fx.SeedUserAsync("target@example.com");
        target.IsActive = operation != "unlock";
        var company = new CompanyProfile { CompanyName = "Acme" };
        var skill = new Skill { Name = "Existing" };
        fx.Db.CompanyProfiles.Add(company);
        fx.Db.Skills.Add(skill);
        await fx.Db.SaveChangesAsync();
        var job = new JobPosting { CompanyProfileId = company.Id, Title = "Engineer", Description = "Work" };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        var userId = target.Id;
        var companyId = company.Id;
        var jobId = job.Id;
        var skillId = skill.Id;
        fx.SignInAdmin(admin.Id, admin.Email!);
        var service = fx.CreateAdminService();
        async Task Execute() 
        {
            ServiceResult result = operation switch
            {
                "approve" => await fx.CreateCompanyAdminService().ApproveAsync(companyId, admin.Id),
                "reject" => await fx.CreateCompanyAdminService().RejectAsync(companyId, admin.Id, "Reason"),
                "lock" or "unlock" => await service.ToggleUserActiveAsync(userId),
                "job" => await service.DeleteJobAsync(jobId),
                "create" => await service.CreateSkillAsync(new CreateSkillRequest { Name = "New skill" }),
                "update" => await service.UpdateSkillAsync(skillId, new UpdateSkillRequest { Name = "Updated" }),
                "delete" => await service.DeleteSkillAsync(skillId),
                _ => throw new InvalidOperationException()
            };
            Assert.True(result.Success);
        }

        await fx.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_audit BEFORE INSERT ON AdminAuditLogs BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END");
        await Assert.ThrowsAsync<DbUpdateException>(Execute);
        Assert.Empty(fx.Db.ChangeTracker.Entries());
        Assert.Equal(CompanyStatus.Pending, (await fx.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == companyId)).Status);
        Assert.Equal(operation != "unlock", (await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).IsActive);
        Assert.True(await fx.Db.JobPostings.AsNoTracking().AnyAsync(j => j.Id == jobId));
        Assert.Equal("Existing", (await fx.Db.Skills.AsNoTracking().SingleAsync(s => s.Id == skillId)).Name);
        Assert.False(await fx.Db.Skills.AnyAsync(s => s.Name == "New skill"));
        Assert.Empty(await fx.Db.AdminAuditLogs.AsNoTracking().ToListAsync());

        await fx.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_audit");
        await Execute();
        Assert.Single(await fx.Db.AdminAuditLogs.AsNoTracking().ToListAsync());
        if (operation is "approve" or "reject")
            Assert.Equal(operation == "approve" ? CompanyStatus.Approved : CompanyStatus.Rejected,
                (await fx.Db.CompanyProfiles.AsNoTracking().SingleAsync(c => c.Id == companyId)).Status);
        if (operation is "lock" or "unlock")
            Assert.Equal(operation == "unlock", (await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).IsActive);
        if (operation == "job") Assert.False(await fx.Db.JobPostings.AnyAsync(j => j.Id == jobId));
        if (operation == "create") Assert.True(await fx.Db.Skills.AnyAsync(s => s.Name == "New skill"));
        if (operation == "update") Assert.True(await fx.Db.Skills.AnyAsync(s => s.Id == skillId && s.Name == "Updated"));
        if (operation == "delete") Assert.False(await fx.Db.Skills.AnyAsync(s => s.Id == skillId));
    }

    [Fact]
    public async Task ToggleUserActive_WhenAuditInsertFails_RollsBackUserChange()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("rollback@example.com");
        fx.SignInAdmin("admin-1", "admin@example.com");
        await fx.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_audit BEFORE INSERT ON AdminAuditLogs BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END");

        await Assert.ThrowsAsync<DbUpdateException>(() => fx.CreateAdminService().ToggleUserActiveAsync(user.Id));

        fx.Db.ChangeTracker.Clear();
        Assert.True((await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).IsActive);
        Assert.Empty(await fx.Db.AdminAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ToggleUserActive_WhenIdentityMissing_DoesNotPersistChange()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("rollback@example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fx.CreateAdminService().ToggleUserActiveAsync(user.Id));

        fx.Db.ChangeTracker.Clear();
        Assert.True((await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).IsActive);
        Assert.Empty(await fx.Db.AdminAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ToggleUserActive_WritesLockThenUnlockEntries()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync("target@example.com");
        fx.SignInAdmin("admin-1", "admin@example.com");
        var service = fx.CreateAdminService();

        var locked = await service.ToggleUserActiveAsync(user.Id);
        var unlocked = await service.ToggleUserActiveAsync(user.Id);

        Assert.True(locked.Success);
        Assert.True(unlocked.Success);
        var logs = await fx.Db.AdminAuditLogs.AsNoTracking().OrderBy(log => log.Id).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal(AuditActions.UserLock, logs[0].Action);
        Assert.Equal(AuditTargetTypes.User, logs[0].TargetType);
        Assert.Equal(user.Id, logs[0].TargetId);
        Assert.Contains("target@example.com", logs[0].Detail);
        Assert.Equal(AuditActions.UserUnlock, logs[1].Action);
        Assert.Equal("admin@example.com", logs[1].AdminEmail);
    }

    [Fact]
    public async Task CompanyApproveThenReject_WritesEntriesWithReason()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = new CompanyProfile { CompanyName = "Acme", Status = CompanyStatus.Pending };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var admin = await fx.SeedUserAsync("admin@example.com", "Str0ng!Pass", AppRoles.Admin);
        fx.SignInAdmin(admin.Id, "admin@example.com");
        var service = fx.CreateCompanyAdminService();

        var approved = await service.ApproveAsync(company.Id, admin.Id);
        var rejected = await service.RejectAsync(company.Id, admin.Id, "Missing business licence");

        Assert.True(approved.Success);
        Assert.True(rejected.Success);
        var logs = await fx.Db.AdminAuditLogs.AsNoTracking().OrderBy(log => log.Id).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal(AuditActions.CompanyApprove, logs[0].Action);
        Assert.Equal(AuditTargetTypes.Company, logs[0].TargetType);
        Assert.Equal(company.Id.ToString(), logs[0].TargetId);
        Assert.Contains("Acme", logs[0].Detail);
        Assert.Equal(AuditActions.CompanyReject, logs[1].Action);
        Assert.Contains("Missing business licence", logs[1].Detail);
    }

    [Fact]
    public async Task DeleteJob_WritesTitleAndCompanyInDetail()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = new CompanyProfile { CompanyName = "Acme", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();
        var job = new JobPosting
        {
            CompanyProfileId = company.Id,
            Title = "Senior Engineer",
            Description = "Build things",
            Status = JobStatus.Published
        };
        fx.Db.JobPostings.Add(job);
        await fx.Db.SaveChangesAsync();
        fx.SignInAdmin("admin-1", "admin@example.com");
        var service = fx.CreateAdminService();

        var result = await service.DeleteJobAsync(job.Id);

        Assert.True(result.Success);
        Assert.Null(await fx.Db.JobPostings.FindAsync(job.Id));
        var log = await fx.Db.AdminAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(AuditActions.JobDelete, log.Action);
        Assert.Equal(AuditTargetTypes.Job, log.TargetType);
        Assert.Equal(job.Id.ToString(), log.TargetId);
        Assert.Contains("Senior Engineer", log.Detail);
        Assert.Contains("Acme", log.Detail);
    }

    [Fact]
    public async Task SkillCreateUpdateDelete_WriteEntries()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.SignInAdmin("admin-1", "admin@example.com");
        var service = fx.CreateAdminService();

        var created = await service.CreateSkillAsync(new CreateSkillRequest { Name = "Rust", Category = "Backend" });
        var updated = await service.UpdateSkillAsync(
            created.Data!.Id, new UpdateSkillRequest { Name = "Rust / Tokio", Category = "Backend" });
        var deleted = await service.DeleteSkillAsync(created.Data.Id);

        Assert.True(created.Success);
        Assert.True(updated.Success);
        Assert.True(deleted.Success);
        var logs = await fx.Db.AdminAuditLogs.AsNoTracking().OrderBy(log => log.Id).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.Equal(
            new[] { AuditActions.SkillCreate, AuditActions.SkillUpdate, AuditActions.SkillDelete },
            logs.Select(log => log.Action));
        Assert.All(logs, log => Assert.Equal(AuditTargetTypes.Skill, log.TargetType));
        Assert.All(logs, log => Assert.Equal(created.Data.Id.ToString(), log.TargetId));
        Assert.Contains("Rust / Tokio", logs[1].Detail);
    }

    [Fact]
    public async Task FailedAdminOperations_WriteNoEntries()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = new CompanyProfile { CompanyName = "Acme", Status = CompanyStatus.Pending };
        fx.Db.CompanyProfiles.Add(company);
        fx.Db.Skills.Add(new Skill { Name = "C#" });
        await fx.Db.SaveChangesAsync();
        fx.SignInAdmin("admin-1", "admin@example.com");
        var service = fx.CreateAdminService();

        var rejectedWithoutReason = await fx.CreateCompanyAdminService().RejectAsync(
            company.Id, "admin-1", "   ");
        var duplicateSkill = await service.CreateSkillAsync(new CreateSkillRequest { Name = "C#" });
        var missingJob = await service.DeleteJobAsync(9999);

        Assert.False(rejectedWithoutReason.Success);
        Assert.False(duplicateSkill.Success);
        Assert.False(missingJob.Success);
        Assert.Equal(0, await fx.Db.AdminAuditLogs.CountAsync());
    }
}
