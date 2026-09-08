namespace hotjob.Models.Job;

public enum JobStatus
{
    Draft,
    Published,
    Closed,
    Expired
}

public class JobPosting
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }
    public int? CreatedByEmployerId { get; set; }
    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public int ExperienceRequired { get; set; }
    public string JobType { get; set; } = default!;
    public DateTime? ExpiryDate { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Users.CompanyProfile Company { get; set; } = default!;
    public Users.Employer? CreatedBy { get; set; }
    public ICollection<JobSkill> JobSkills { get; set; } = new List<JobSkill>();
    public ICollection<JobCategoryMapping> CategoryMappings { get; set; } = new List<JobCategoryMapping>();
    public ICollection<Application.Application> Applications { get; set; } = new List<Application.Application>();
    public ICollection<SavedJob> SavedBy { get; set; } = new List<SavedJob>();
    public ICollection<ML.MatchScore> MatchScores { get; set; } = new List<ML.MatchScore>();
}
