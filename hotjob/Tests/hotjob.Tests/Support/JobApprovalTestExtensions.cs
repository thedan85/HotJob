using hotjob.Data;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.Support;

/// <summary>Runs the admin side of the two-step job approval flow inside tests.</summary>
public static class JobApprovalTestExtensions
{
    private const string ApproverEmail = "job-approver@example.com";

    public static async Task ApproveJobAsync(this TestFixture fx, int jobId)
    {
        var result = await fx.CreateJobAdminService().ApproveAsync(jobId, await SignInApproverAsync(fx));
        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    public static async Task RejectJobAsync(this TestFixture fx, int jobId, string reason = "Not acceptable")
    {
        var result = await fx.CreateJobAdminService().RejectAsync(jobId, await SignInApproverAsync(fx), reason);
        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    /// <summary>Ensures an approver admin exists and is the acting (audited) administrator.</summary>
    private static async Task<string> SignInApproverAsync(TestFixture fx)
    {
        var existing = await fx.Db.Users
            .Where(user => user.Email == ApproverEmail)
            .Select(user => user.Id)
            .FirstOrDefaultAsync();
        var id = existing ?? (await fx.SeedUserAsync(ApproverEmail, role: AppRoles.Admin)).Id;
        // Circuit path: HttpContextAccessor is AsyncLocal and would not survive returning from this async helper.
        fx.SignInAdminOnCircuit(id, ApproverEmail);
        return id;
    }
}
