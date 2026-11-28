namespace hotjob.Services.Email.Templates;

public sealed record EmailVerificationEmailModel(
    string ActionUrl,
    TimeSpan? TokenLifetime = null,
    string? RecipientName = null);

/// <summary>
/// Welcome and email-verification message. The caller supplies the action URL and,
/// when available, the token lifetime configured by the identity token provider.
/// </summary>
public static class EmailVerificationTemplate
{
    public const string Subject = "Verify your HotJob email address";

    public static EmailTemplate Render(EmailVerificationEmailModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var expiryText = model.TokenLifetime is { } lifetime
            ? $"This verification link expires in {EmailTokenLifetime.Describe(lifetime)}. " +
              "If it expires, request a new verification email from your account."
            : null;

        return new EmailTemplate(Subject, EmailLayout.Render(new EmailLayoutContent(
            Preheader: "Welcome to HotJob. Confirm your email address to finish setting up your account.",
            Eyebrow: "Welcome to HotJob",
            Heading: "Verify your email address",
            Paragraphs:
            [
                EmailCopy.Greeting(model.RecipientName),
                "Welcome to HotJob. Thanks for creating an account with us.",
                "Please confirm this email address so we can keep your account secure and make sure you receive important updates about your job search.",
                "Click the button below to verify your email address."
            ],
            ActionLabel: "Verify email address",
            ActionUrl: model.ActionUrl,
            ExpiryText: expiryText,
            SecurityNotice: "If you did not create a HotJob account, you can ignore this email. " +
                            "No account will be verified unless you click the button above.",
            FooterNote: "You received this email because someone used this address to create a HotJob account.")));
    }
}
