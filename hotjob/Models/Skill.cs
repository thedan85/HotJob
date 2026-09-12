using System.ComponentModel.DataAnnotations;

namespace hotjob.Models;

/// <summary>Canonical skill shared by resumes and job postings.</summary>
public class Skill
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = default!;

    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>Resumes listing this skill.</summary>
    public ICollection<Resume.ResumeSkill> ResumeSkills { get; set; } = new List<Resume.ResumeSkill>();

    /// <summary>Postings requiring this skill.</summary>
    public ICollection<Job.JobSkill> JobSkills { get; set; } = new List<Job.JobSkill>();
}
