namespace hotjob.Models.Job;

public class JobSkill
{
    public int JobPostingId { get; set; }
    public int SkillId { get; set; }
    public double ImportanceWeight { get; set; } = 1.0;

    public JobPosting JobPosting { get; set; } = default!;
    public Skill Skill { get; set; } = default!;
}
