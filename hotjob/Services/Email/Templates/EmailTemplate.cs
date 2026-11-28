namespace hotjob.Services.Email.Templates;

/// <summary>
/// A rendered transactional email: a fixed subject plus a complete HTML document.
/// </summary>
public sealed record EmailTemplate(string Subject, string Html);

/// <summary>
/// Content for the shared HotJob email shell. All string values are HTML-encoded by
/// <see cref="EmailLayout"/>; <see cref="Paragraphs"/> contains plain text only.
/// </summary>
public sealed record EmailLayoutContent(
    string Preheader,
    string Eyebrow,
    string Heading,
    IReadOnlyList<string> Paragraphs,
    string ActionLabel,
    string ActionUrl,
    string? ExpiryText = null,
    string? SecurityNotice = null,
    string? FooterNote = null);
