namespace hotjob.Services.Email.Templates;

public sealed record PasswordResetEmailModel(
    string ActionUrl,
    TimeSpan TokenLifetime,
    string? RecipientName = null);

/// <summary>
/// Password-reset message. The caller passes the configured reset-token lifetime so
/// the copy always matches the value enforced by the recovery service.
/// </summary>
public static class PasswordResetTemplate
{
    public const string Subject = "Reset your HotJob password";

    public static EmailTemplate Render(PasswordResetEmailModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new EmailTemplate(Subject, EmailLayout.Render(new EmailLayoutContent(
            Preheader: "Use the link in this email to choose a new HotJob password.",
            Eyebrow: "Password reset",
            Heading: "Reset your password",
            Paragraphs:
            [
                EmailCopy.Greeting(model.RecipientName),
                "We received a request to reset the password for your HotJob account. " +
                "You can choose a new password using the button below.",
                "If you did not ask to reset your password, there is nothing you need to do."
            ],
            ActionLabel: "Reset password",
            ActionUrl: model.ActionUrl,
            ExpiryText: $"This password reset link expires in {EmailTokenLifetime.Describe(model.TokenLifetime)}.",
            SecurityNotice: "Security note: If you ignore this email, your password will not change. " +
                            "HotJob will never ask you for your password by email.",
            FooterNote: "You received this email because a password reset was requested for your HotJob account.")));
    }
}
