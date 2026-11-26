using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Authentication;

public sealed class ResetPasswordRequest
{
    public string? Email { get; set; }
    public string? Token { get; set; }
    [Required]
    public string NewPassword { get; set; } = "";
}
