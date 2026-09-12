using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Job;

/// <summary>Skill required by a job posting with a relative importance weight.</summary>
public class JobSkill
{
    public int JobPostingId { get; set; }

    public int SkillId { get; set; }

    [Range(0, 10)]
    public double ImportanceWeight { get; set; } = 1.0;

    /// <summary>Posting requiring the skill.</summary>
    public JobPosting JobPosting { get; set; } = default!;

    /// <summary>Required skill.</summary>
    public Skill Skill { get; set; } = default!;
}
