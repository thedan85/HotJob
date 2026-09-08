namespace hotjob.Models.Resume;

public class Resume
{
    public int Id { get; set; }
    public int CandidateId { get; set; }
    public string Title { get; set; } = default!;
    public string? Summary { get; set; }
    public string? FileUrl { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Users.Candidate Candidate { get; set; } = default!;
    public ICollection<Education> Educations { get; set; } = new List<Education>();
    public ICollection<WorkExperience> WorkExperiences { get; set; } = new List<WorkExperience>();
    public ICollection<ResumeSkill> ResumeSkills { get; set; } = new List<ResumeSkill>();
    public ICollection<Application.Application> Applications { get; set; } = new List<Application.Application>();
}
