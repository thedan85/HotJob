namespace hotjob.Models.Resume;

public class ResumeSkill
{
    public int ResumeId { get; set; }
    public int SkillId { get; set; }
    public int YearsOfExperience { get; set; }

    public Resume Resume { get; set; } = default!;
    public Skill Skill { get; set; } = default!;
}
