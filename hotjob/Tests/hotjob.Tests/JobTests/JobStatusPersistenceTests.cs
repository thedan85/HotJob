using hotjob.Models.Job;

namespace hotjob.Tests.JobTests;

/// <summary>
/// <see cref="JobStatus"/> is stored as an int. Renumbering it silently changes the meaning of
/// every existing JobPostings row, so the values are pinned here.
/// </summary>
public sealed class JobStatusPersistenceTests
{
    [Theory]
    [InlineData(JobStatus.Draft, 0)]
    [InlineData(JobStatus.Published, 1)]
    [InlineData(JobStatus.Closed, 2)]
    [InlineData(JobStatus.Expired, 3)]
    [InlineData(JobStatus.PendingApproval, 4)]
    [InlineData(JobStatus.Approved, 5)]
    [InlineData(JobStatus.Rejected, 6)]
    public void JobStatus_KeepsItsStoredValue(JobStatus status, int stored) =>
        Assert.Equal(stored, (int)status);

    [Fact]
    public void JobStatus_HasNoUnpinnedMembers() =>
        Assert.Equal(7, Enum.GetValues<JobStatus>().Length);
}
