namespace hotjob.Models.Users;

public enum MembershipStatus
{
    Pending,
    Active,
    Rejected
}

public class Employer
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public int CompanyProfileId { get; set; }
    public string FullName { get; set; } = default!;
    public string? Position { get; set; }
    public bool IsOwner { get; set; }
    public MembershipStatus MembershipStatus { get; set; } = MembershipStatus.Pending;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = default!;
    public CompanyProfile Company { get; set; } = default!;
    public ICollection<Job.JobPosting> CreatedJobs { get; set; } = new List<Job.JobPosting>();
}
