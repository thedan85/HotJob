using System.ComponentModel.DataAnnotations;
using hotjob.Components.Account;

namespace hotjob.Tests;

public sealed class ChangePasswordFormTests
{
    [Theory]
    [InlineData(nameof(ChangePasswordForm.CurrentPassword))]
    [InlineData(nameof(ChangePasswordForm.NewPassword))]
    [InlineData(nameof(ChangePasswordForm.ConfirmNewPassword))]
    public void Validate_MissingField_ReportsRequiredField(string field)
    {
        var form = ValidForm();
        typeof(ChangePasswordForm).GetProperty(field)!.SetValue(form, "");

        var errors = Validate(form);

        Assert.Contains(errors, error => error.MemberNames.Contains(field));
    }

    [Fact]
    public void Validate_MismatchedConfirmation_ReportsConfirmation()
    {
        var form = ValidForm();
        form.ConfirmNewPassword = "new123!Pass";

        var errors = Validate(form);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(form.ConfirmNewPassword)));
    }

    [Fact]
    public void Validate_UnchangedPassword_ReportsNewPassword()
    {
        var form = ValidForm();
        form.NewPassword = form.CurrentPassword;
        form.ConfirmNewPassword = form.NewPassword;

        var errors = Validate(form);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(form.NewPassword)));
    }

    [Fact]
    public void Validate_MatchingDifferentPassword_IsValid()
    {
        Assert.Empty(Validate(ValidForm()));
    }

    [Fact]
    public void Validate_PasswordPolicy_IsLeftToAccountService()
    {
        var form = ValidForm();
        form.NewPassword = "weak";
        form.ConfirmNewPassword = "weak";

        Assert.Empty(Validate(form));
    }

    private static ChangePasswordForm ValidForm() => new()
    {
        CurrentPassword = "Str0ng!Pass",
        NewPassword = "New123!Pass",
        ConfirmNewPassword = "New123!Pass"
    };

    private static List<ValidationResult> Validate(ChangePasswordForm form)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), errors, true);
        return errors;
    }
}
