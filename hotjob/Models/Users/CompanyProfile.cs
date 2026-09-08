namespace hotjob.Models.Users;

public enum CompanyStatus
{
    Pending,
    Approved,
    Rejected
}

public class CompanyProfile
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = default!;
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }
    public string? Location { get; set; }
    public string? Industry { get; set; }
    public string? Scale { get; set; }
    public string? Description { get; set; }
    public CompanyStatus Status { get; set; } = CompanyStatus.Pending;
    public string? RejectionReason { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedByAdminId { get; set; }

    public ICollection<Employer> Employers { get; set; } = new List<Employer>();
    public ICollection<Job.JobPosting> JobPostings { get; set; } = new List<Job.JobPosting>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
