using System.ComponentModel.DataAnnotations;

namespace hotjob.Components.Account;

public sealed class ResetPasswordForm
{
    // Identity owns password policy. These attributes only validate form completeness.
    [Required(ErrorMessage = "New password is required.")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Confirm your new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}
