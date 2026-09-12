using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Resume;

/// <summary>Candidate resume holding education, experience and skills.</summary>
public class Resume
{
    public int Id { get; set; }

    public int CandidateId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(2000)]
    public string? Summary { get; set; }

    [Url, MaxLength(2048)]
    public string? FileUrl { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Candidate owning this resume.</summary>
    public Users.Candidate Candidate { get; set; } = default!;

    /// <summary>Education entries on the resume.</summary>
    public ICollection<Education> Educations { get; set; } = new List<Education>();

    /// <summary>Work experience entries on the resume.</summary>
    public ICollection<WorkExperience> WorkExperiences { get; set; } = new List<WorkExperience>();

    /// <summary>Skills attached to the resume.</summary>
    public ICollection<ResumeSkill> ResumeSkills { get; set; } = new List<ResumeSkill>();

    /// <summary>Applications submitted with this resume.</summary>
    public ICollection<Application.Application> Applications { get; set; } = new List<Application.Application>();

    /// <summary>ML match scores computed for this resume.</summary>
    public ICollection<ML.MatchScore> MatchScores { get; set; } = new List<ML.MatchScore>();
}
