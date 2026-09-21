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

public sealed record RegisteredAccountResponse(
    string UserId,
    string Email,
    bool HasCandidateProfile,
    bool HasEmployerProfile,
    int? CompanyProfileId);