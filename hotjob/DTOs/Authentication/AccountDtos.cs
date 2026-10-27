using System.ComponentModel.DataAnnotations;
using hotjob.Models.Users;

namespace hotjob.DTOs.Authentication;

public sealed class RegisterAccountRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(100)]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).+$",
        ErrorMessage = "Password must contain an uppercase letter, lowercase letter, number, and special character.")]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Position { get; set; }

    [Required]
    public AccountType AccountType { get; set; }

    public int? CompanyProfileId { get; set; }

    [MaxLength(200)]
    public string? CompanyName { get; set; }
}

public enum AccountType
{
    Candidate = 1,
    Employer = 2
}

/// <summary>Candidate-to-employer upgrade. No email/password: the caller is already signed in.</summary>
public sealed class UpgradeToEmployerRequest
{
    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Position { get; set; }

    public int? CompanyProfileId { get; set; }

    [MaxLength(200)]
    public string? CompanyName { get; set; }
}

public sealed record RegisteredAccountResponse(
    string UserId,
    string Email,
    bool HasCandidateProfile,
    bool HasEmployerProfile,
    int? CompanyProfileId);

public sealed class CandidateProfileDto
{
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? Address { get; set; }
    public string? AvatarUrl { get; set; }
    public string? GithubUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? Position { get; set; }
    public string? Summary { get; set; }
    public string? JobSeekingStatus { get; set; }
    public string? PreferredWorkModel { get; set; }
    public string? PreferredLocation { get; set; }

    /// <summary>Whether approved employers can find this candidate in candidate search.</summary>
    public bool IsDiscoverable { get; set; }
}

public sealed class EmployerProfileDto
{
    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public int CompanyProfileId { get; set; }
    public string? CompanyName { get; set; }
    public bool IsOwner { get; set; }
    public MembershipStatus MembershipStatus { get; set; }
}

public sealed record ProfileResponse(
    string UserId,
    string Email,
    bool EmailConfirmed,
    bool HasCandidateProfile,
    bool HasEmployerProfile,
    CandidateProfileDto? Candidate,
    EmployerProfileDto? Employer);

public sealed class UpdateProfileRequest
{
    [MaxLength(100)]
    public string? FullName { get; set; }

    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [Url, MaxLength(2048)]
    public string? AvatarUrl { get; set; }

    [Url, MaxLength(2048)]
    public string? GithubUrl { get; set; }

    [Url, MaxLength(2048)]
    public string? LinkedInUrl { get; set; }

    [MaxLength(100)]
    public string? Position { get; set; }

    [MaxLength(2000)]
    public string? Summary { get; set; }

    [MaxLength(50)]
    public string? JobSeekingStatus { get; set; }

    [MaxLength(50)]
    public string? PreferredWorkModel { get; set; }

    [MaxLength(500)]
    public string? PreferredLocation { get; set; }

    [EmailAddress, MaxLength(256)]
    public string? Email { get; set; }
}

public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(100)]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).+$",
        ErrorMessage = "Password must contain an uppercase letter, lowercase letter, number, and special character.")]
    public string NewPassword { get; set; } = string.Empty;
}