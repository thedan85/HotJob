using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Users;

/// <summary>Candidate gender.</summary>
public enum Gender
{
    Male,
    Female,
    Other,
    PreferNotToSay
}

/// <summary>Candidate profile linked one-to-one to an application user.</summary>
public class Candidate
{
    public int Id { get; set; }

    public string UserId { get; set; } = default!;

    [Required, MaxLength(100)]
    public string FullName { get; set; } = default!;

    public DateOnly? DateOfBirth { get; set; }

    public Gender? Gender { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [Url, MaxLength(2048)]
    public string? AvatarUrl { get; set; }

    [Url, MaxLength(2048)]
    public string? GithubUrl { get; set; }

    [Url, MaxLength(2048)]
    public string? LinkedInUrl { get; set; }

    /// <summary>Identity user owning this profile.</summary>
    public ApplicationUser User { get; set; } = default!;

    /// <summary>Resumes created by the candidate.</summary>
    public ICollection<Resume.Resume> Resumes { get; set; } = new List<Resume.Resume>();

    /// <summary>Job applications submitted by the candidate.</summary>
    public ICollection<Application.Application> Applications { get; set; } = new List<Application.Application>();

    /// <summary>Jobs bookmarked by the candidate.</summary>
    public ICollection<SavedJob> SavedJobs { get; set; } = new List<SavedJob>();

    /// <summary>Company reviews written by the candidate.</summary>
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
