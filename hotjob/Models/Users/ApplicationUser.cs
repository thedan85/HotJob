using Microsoft.AspNetCore.Identity;

namespace hotjob.Models.Users;

/// <summary>Identity user of the system. A user may have a candidate profile, an employer profile, or both.</summary>
public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    /// <summary>Candidate profile, if the user registered as a candidate.</summary>
    public Candidate? Candidate { get; set; }

    /// <summary>Employer profile, if the user registered as an employer.</summary>
    public Employer? Employer { get; set; }

    /// <summary>Notifications sent to the user.</summary>
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    /// <summary>Persisted refresh-token sessions for this user.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
