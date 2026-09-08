namespace hotjob.Models.Users;

public class Candidate
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Address { get; set; }
    public string? AvatarUrl { get; set; }
    public string? GithubUrl { get; set; }
    public string? LinkedInUrl { get; set; }

    public ApplicationUser User { get; set; } = default!;
    public ICollection<Resume.Resume> Resumes { get; set; } = new List<Resume.Resume>();
    public ICollection<SavedJob> SavedJobs { get; set; } = new List<SavedJob>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
