using Microsoft.AspNetCore.Identity;

namespace hotjob.Models.Users;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public Candidate? Candidate { get; set; }
    public Employer? Employer { get; set; }
}
