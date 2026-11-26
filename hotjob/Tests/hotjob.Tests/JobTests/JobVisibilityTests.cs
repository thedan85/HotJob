using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Repositories.Job;
using hotjob.Services.Billing;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.JobTests;

/// <summary>Jobs of a deactivated (or no longer approved) company disappear from every candidate-facing path.</summary>
public sealed class JobVisibilityTests
{
    private static JobService CreateJobService(TestFixture fx) => new(
        fx.Db,
        new JobPostingRepository(fx.Db),
        new EntitlementService(fx.Db, TimeProvider.System),
        new StubJobAlertService(),
        NullLogger<JobService>.Instance, TimeProvider.System);

    private static Task<ServiceResult<hotjob.DTOs.Job.PagedResponse<hotjob.DTOs.Job.PublicJobListItemResponse>>> BrowseAsync(JobService service) =>
        service.BrowsePublishedAsync(null, null, null, null, null, null, null, null, 1, 50);

    [Theory]
    [InlineData("deactivated")]
    [InlineData("rejected")]
    public async Task JobsOfAnInactiveCompany_AreHiddenAndClosedToApplications(string companyState)
    {
        await using var fx = await TestFixture.CreateAsync();
        var visibleCompany = await MatchTestData.CreateCompanyAsync(fx);
        var hiddenCompany = await MatchTestData.CreateCompanyAsync(fx);
        var visible = await MatchTestData.CreateJobAsync(fx, visibleCompany.Id, [], title: "Visible");
        var hidden = await MatchTestData.CreateJobAsync(fx, hiddenCompany.Id, [], title: "Hidden");
        var (candidate, resume) = await MatchTestData.CreateCandidateAsync(fx, "candidate@example.com");
        if (companyState == "deactivated")
            hiddenCompany.IsActive = false;
        else
            hiddenCompany.Status = CompanyStatus.Rejected;
        await fx.Db.SaveChangesAsync();
        var jobs = CreateJobService(fx);

        var browse = await BrowseAsync(jobs);
        var detail = await jobs.GetPublishedAsync(hidden.Id);
        var apply = await new ApplicationService(fx.Db, TimeProvider.System, NullLogger<ApplicationService>.Instance)
            .ApplyAsync(candidate.UserId, hidden.Id, resume.Id, null);

        Assert.Equal(new[] { visible.Id }, browse.Data!.Items.Select(item => item.Id));
        Assert.Equal(ServiceErrorType.NotFound, detail.ErrorType);
        Assert.Equal(ServiceErrorType.Conflict, apply.ErrorType);
    }

    [Fact]
    public async Task ReactivatingTheCompany_BringsItsJobsBack()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var job = await MatchTestData.CreateJobAsync(fx, company.Id, []);
        company.IsActive = false;
        await fx.Db.SaveChangesAsync();
        var jobs = CreateJobService(fx);
        Assert.Empty((await BrowseAsync(jobs)).Data!.Items);

        company.IsActive = true;
        await fx.Db.SaveChangesAsync();

        Assert.Equal(job.Id, Assert.Single((await BrowseAsync(jobs)).Data!.Items).Id);
        Assert.True((await jobs.GetPublishedAsync(job.Id)).Success);
    }

    [Fact]
    public async Task EmployerStatusCounts_CountOnlyTheirCompany_IncludingApprovalStates()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await MatchTestData.CreateCompanyAsync(fx);
        var other = await MatchTestData.CreateCompanyAsync(fx);
        var employer = await MatchTestData.CreateEmployerAsync(fx, company.Id, "hr@example.com");
        foreach (var status in new[] { JobStatus.Published, JobStatus.Published, JobStatus.PendingApproval, JobStatus.Rejected })
        {
            await MatchTestData.CreateJobAsync(fx, company.Id, [], status: status);
        }
        await MatchTestData.CreateJobAsync(fx, other.Id, [], status: JobStatus.Published);

        var counts = await CreateJobService(fx).GetStatusCountsAsync(employer.UserId);

        Assert.Equal(2, counts.Data![JobStatus.Published]);
        Assert.Equal(1, counts.Data[JobStatus.PendingApproval]);
        Assert.Equal(1, counts.Data[JobStatus.Rejected]);
        Assert.Equal(0, counts.Data[JobStatus.Approved]);
        Assert.Equal(Enum.GetValues<JobStatus>().Length, counts.Data.Count);
    }
}
