using System.Net;

namespace hotjob.Services.Email.Templates;

/// <summary>
/// Validates and encodes links placed in transactional emails.
/// </summary>
public static class EmailActionUrl
{
    /// <summary>
    /// Returns the original absolute HTTP(S) URL, or throws when the value is missing,
    /// relative, or uses any other scheme (for example <c>javascript:</c> or <c>data:</c>).
    /// </summary>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("An absolute HTTP or HTTPS action URL is required.", nameof(url));

        var trimmed = url.Trim();
        if (trimmed.Any(char.IsControl))
            throw new ArgumentException("The action URL contains invalid control characters.", nameof(url));

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The action URL must be an absolute HTTP or HTTPS URL.", nameof(url));
        }

        // Return the original text so percent-encoding produced by the caller is preserved.
        return trimmed;
    }

    /// <summary>Encodes a validated URL for use in an HTML attribute or text node.</summary>
    public static string Encode(string? url) => WebUtility.HtmlEncode(Normalize(url));
}
