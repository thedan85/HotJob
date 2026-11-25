using hotjob.Data;
using hotjob.Services.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.Support;

/// <summary>Runs the admin side of the two-step job approval flow inside tests.</summary>
public static class JobApprovalTestExtensions
{
    private const string ApproverEmail = "job-approver@example.com";

    public static async Task ApproveJobAsync(this TestFixture fx, int jobId)
    {
        var result = await new JobAdminService(fx.Db, NullLogger<JobAdminService>.Instance)
            .ApproveAsync(jobId, await ApproverIdAsync(fx));
        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    public static async Task RejectJobAsync(this TestFixture fx, int jobId, string reason = "Not acceptable")
    {
        var result = await new JobAdminService(fx.Db, NullLogger<JobAdminService>.Instance)
            .RejectAsync(jobId, await ApproverIdAsync(fx), reason);
        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    private static async Task<string> ApproverIdAsync(TestFixture fx)
    {
        var existing = await fx.Db.Users
            .Where(user => user.Email == ApproverEmail)
            .Select(user => user.Id)
            .FirstOrDefaultAsync();
        return existing ?? (await fx.SeedUserAsync(ApproverEmail, role: AppRoles.Admin)).Id;
    }
}
