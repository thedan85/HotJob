using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Resume;

/// <summary>Links a resume to a skill (many-to-many join).</summary>
public class ResumeSkill
{
    public int ResumeId { get; set; }

    public int SkillId { get; set; }

    [Range(0, 50)]
    public int YearsOfExperience { get; set; }

    /// <summary>Resume owning the skill.</summary>
    public Resume Resume { get; set; } = default!;

    /// <summary>Skill attached to the resume.</summary>
    public Skill Skill { get; set; } = default!;
}
