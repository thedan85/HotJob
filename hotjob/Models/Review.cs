using System.ComponentModel.DataAnnotations;

namespace hotjob.Models;

/// <summary>Review a candidate wrote about a company.</summary>
public class Review
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    public int CandidateId { get; set; }

    [Range(1, 5)]
    public int RatingScore { get; set; }

    [MaxLength(2000)]
    public string? ReviewText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Company being reviewed.</summary>
    public Users.CompanyProfile Company { get; set; } = default!;

    /// <summary>Candidate who wrote the review.</summary>
    public Users.Candidate Candidate { get; set; } = default!;
}
