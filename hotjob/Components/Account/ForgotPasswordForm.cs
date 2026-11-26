using System.ComponentModel.DataAnnotations;

namespace hotjob.Components.Account;

public sealed class ForgotPasswordForm
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
}
