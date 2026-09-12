using Microsoft.AspNetCore.Identity;

namespace hotjob.Models.Users;

/// <summary>Identity user of the system. Each user is either a candidate or an employer.</summary>
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
}
