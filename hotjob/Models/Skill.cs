namespace hotjob.Models;

public class Skill
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Category { get; set; }

    public ICollection<Resume.ResumeSkill> ResumeSkills { get; set; } = new List<Resume.ResumeSkill>();
    public ICollection<Job.JobSkill> JobSkills { get; set; } = new List<Job.JobSkill>();
}
