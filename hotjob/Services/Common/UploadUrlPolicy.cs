namespace hotjob.Services.Common;

/// <summary>Validates links rendered from stored upload paths.</summary>
public static class UploadUrlPolicy
{
    /// <summary>Local URL prefix shared with CV upload storage.</summary>
    public const string LocalPrefix = "/uploads/cv/";

    /// <summary>Accepts HTTP(S) links and local CV upload paths; rejects script, traversal and query/fragment tricks.</summary>
    public static bool IsSafeSnapshotUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048)
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        const string prefix = LocalPrefix;
        if (!url.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (url.Contains('\\') || url.Contains('?') || url.Contains('#') || url.Contains('%')
            || url.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        return url[prefix.Length..]
            .Split('/')
            .All(segment => segment.Length > 0 && segment is not ("." or ".."));
    }
}
