namespace hotjob.Models;

public class Review
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }
    public int CandidateId { get; set; }
    public int RatingScore { get; set; }
    public string? ReviewText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Users.CompanyProfile Company { get; set; } = default!;
    public Users.Candidate Candidate { get; set; } = default!;
}
