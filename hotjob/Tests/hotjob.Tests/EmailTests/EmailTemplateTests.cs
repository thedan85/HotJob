using hotjob.Services.Email.Templates;

namespace hotjob.Tests.EmailTests;

public sealed class EmailTemplateTests
{
    private const string ActionUrl =
        "https://hotjob.example/reset-password?email=a%2Bb%40example.test&token=abc%2Fdef%3D";

    [Fact]
    public void PasswordReset_RendersBrandedLayoutAndConfiguredLifetime()
    {
        var email = PasswordResetTemplate.Render(
            new PasswordResetEmailModel(ActionUrl, TimeSpan.FromMinutes(30)));

        Assert.Equal("Reset your HotJob password", email.Subject);
        Assert.Contains("<!doctype html>", email.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-width:600px", email.Html);
        Assert.Contains("#F7F6F2", email.Html);
        Assert.Contains("#0E3B2E", email.Html);
        Assert.Contains("#E4572E", email.Html);
        Assert.Contains("hotjob", email.Html);
        Assert.Contains("Reset your password", email.Html);
        Assert.Contains("Reset password", email.Html);
        Assert.Contains("30 minutes", email.Html);
        Assert.Contains("password will not change", email.Html);
        Assert.Contains("copy and paste this link", email.Html);
    }

    [Fact]
    public void Verification_RendersWelcomeExpiryAndIgnoreNotice()
    {
        var email = EmailVerificationTemplate.Render(
            new EmailVerificationEmailModel(ActionUrl, TimeSpan.FromDays(1)));

        Assert.Equal("Verify your HotJob email address", email.Subject);
        Assert.Contains("Welcome to HotJob", email.Html);
        Assert.Contains("Verify your email address", email.Html);
        Assert.Contains("Verify email address", email.Html);
        Assert.Contains("1 day", email.Html);
        Assert.Contains("ignore this email", email.Html);
    }

    [Fact]
    public void Verification_OmitsExpiryWhenLifetimeIsUnknown()
    {
        var email = EmailVerificationTemplate.Render(new EmailVerificationEmailModel(ActionUrl));

        Assert.DoesNotContain("expires in", email.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(30, "30 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "90 minutes")]
    [InlineData(1440, "1 day")]
    [InlineData(2880, "2 days")]
    public void TokenLifetime_UsesConfiguredValue(int minutes, string expected)
    {
        Assert.Equal(expected, EmailTokenLifetime.Describe(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void TokenLifetime_RejectsNonPositiveValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailTokenLifetime.Describe(TimeSpan.Zero));
    }

    [Fact]
    public void ActionUrl_IsEncodedOnceInHrefAndFallbackText()
    {
        var email = PasswordResetTemplate.Render(
            new PasswordResetEmailModel(ActionUrl, TimeSpan.FromMinutes(30)));

        const string encoded =
            "https://hotjob.example/reset-password?email=a%2Bb%40example.test&amp;token=abc%2Fdef%3D";

        Assert.Contains($"href=\"{encoded}\"", email.Html);
        Assert.Contains($">{encoded}</a>", email.Html);
        Assert.DoesNotContain("&token=", email.Html);
        Assert.DoesNotContain("&amp;amp;", email.Html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("/relative/path")]
    [InlineData("")]
    [InlineData("   ")]
    public void ActionUrl_RejectsUnsafeOrRelativeValues(string url)
    {
        Assert.Throws<ArgumentException>(() => EmailActionUrl.Normalize(url));
    }

    [Fact]
    public void DynamicCopy_IsHtmlEncoded()
    {
        var email = EmailVerificationTemplate.Render(new EmailVerificationEmailModel(
            ActionUrl, TimeSpan.FromDays(1), "Ada <script>alert(\"x\")</script> & Co"));

        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains(
            "Ada &lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; Co",
            email.Html);
    }

    [Fact]
    public void Layout_EncodesEveryDynamicTextSlot()
    {
        var html = EmailLayout.Render(new EmailLayoutContent(
            Preheader: "<pre>",
            Eyebrow: "<eye>",
            Heading: "<h1>Hi</h1>",
            Paragraphs: ["<p>"],
            ActionLabel: "<click>",
            ActionUrl: "https://example.test/",
            ExpiryText: "<exp>",
            SecurityNotice: "<sec>",
            FooterNote: "<foot>"));

        Assert.DoesNotContain("<h1>", html);
        Assert.DoesNotContain("<pre>", html);
        Assert.Contains("&lt;h1&gt;Hi&lt;/h1&gt;", html);
        Assert.Contains("&lt;click&gt;", html);
        Assert.Contains("&lt;exp&gt;", html);
        Assert.Contains("&lt;sec&gt;", html);
        Assert.Contains("&lt;foot&gt;", html);
    }

    [Fact]
    public void Templates_UseEmailCompatibleMarkupWithoutActiveContent()
    {
        var templates = new[]
        {
            PasswordResetTemplate.Render(
                new PasswordResetEmailModel(ActionUrl, TimeSpan.FromMinutes(30))),
            EmailVerificationTemplate.Render(
                new EmailVerificationEmailModel(ActionUrl, TimeSpan.FromDays(1)))
        };

        foreach (var email in templates)
        {
            Assert.Contains("role=\"presentation\"", email.Html);
            Assert.Contains("name=\"viewport\"", email.Html);
            Assert.Contains("width=\"100%\"", email.Html);
            Assert.DoesNotContain("<script", email.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<link", email.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", email.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@import", email.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("url(", email.Html, StringComparison.OrdinalIgnoreCase);
        }
    }
}
