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

    [MaxLength(255)]
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

    [MaxLength(200)]
    public string? Position { get; set; }

    [MaxLength(2000)]
    public string? Summary { get; set; }

    [MaxLength(50)]
    public string? JobSeekingStatus { get; set; }

    [MaxLength(50)]
    public string? PreferredWorkModel { get; set; }

    [MaxLength(500)]
    public string? PreferredLocation { get; set; }

    /// <summary>Premium plan last bought. Null if the candidate never subscribed.</summary>
    public int? PremiumPlanId { get; set; }

    /// <summary>When the current uninterrupted Premium period began.</summary>
    public DateTime? PremiumStartedAt { get; set; }

    /// <summary>Premium is active while this is in the future.</summary>
    public DateTime? PremiumUntil { get; set; }

    /// <summary>Profile is boosted in employer candidate search while this is in the future.</summary>
    public DateTime? BoostUntil { get; set; }

    /// <summary>When true, approved employers can find this candidate in candidate search.</summary>
    public bool IsDiscoverable { get; set; }

    /// <summary>Premium plan last bought.</summary>
    public Billing.Plan? PremiumPlan { get; set; }

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
