using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Job;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.JobTests;

public sealed class JobExpiryTests
{
    [Fact]
    public async Task ExpireAsync_OnlyPublishedPastDeadline()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = new CompanyProfile { CompanyName = "Expiry Co", Status = CompanyStatus.Approved };
        fx.Db.CompanyProfiles.Add(company);
        await fx.Db.SaveChangesAsync();

        var now = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        fx.Db.JobPostings.AddRange(
            NewJob(company.Id, "Past deadline", JobStatus.Published, now.AddDays(-1)),
            NewJob(company.Id, "No deadline", JobStatus.Published, null),
            NewJob(company.Id, "Future deadline", JobStatus.Published, now.AddDays(1)),
            NewJob(company.Id, "Already closed", JobStatus.Closed, now.AddDays(-1)),
            NewJob(company.Id, "Draft past deadline", JobStatus.Draft, now.AddDays(-1)));
        await fx.Db.SaveChangesAsync();

        var expired = await JobExpiryService.ExpireAsync(fx.Db, now);

        Assert.Equal(1, expired);
        var statuses = await fx.Db.JobPostings
            .AsNoTracking()
            .OrderBy(job => job.Title)
            .ToDictionaryAsync(job => job.Title, job => job.Status);
        Assert.Equal(JobStatus.Expired, statuses["Past deadline"]);
        Assert.Equal(JobStatus.Published, statuses["No deadline"]);
        Assert.Equal(JobStatus.Published, statuses["Future deadline"]);
        Assert.Equal(JobStatus.Closed, statuses["Already closed"]);
        Assert.Equal(JobStatus.Draft, statuses["Draft past deadline"]);
    }

    private static JobPosting NewJob(int companyId, string title, JobStatus status, DateTime? expiryDate) =>
        new()
        {
            CompanyProfileId = companyId,
            Title = title,
            Description = "d",
            Status = status,
            ExpiryDate = expiryDate
        };
}
