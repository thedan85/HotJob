using System.ComponentModel.DataAnnotations;

namespace hotjob.Components.Account;

public sealed class ChangePasswordForm : IValidatableObject
{
    [Required(ErrorMessage = "Current password is required.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "New password and confirmation must match.")]
    public string ConfirmNewPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Password strength is validated only by AccountService and Identity.
        if (string.Equals(NewPassword, CurrentPassword, StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                "New password must differ from your current password.",
                [nameof(NewPassword)]);
        }
    }
}
