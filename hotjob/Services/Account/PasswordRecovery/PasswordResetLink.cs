namespace hotjob.Services.Account.PasswordRecovery;

public sealed class PasswordResetLink(IConfiguration configuration)
{
    public string? ResolveBaseUrl(string fallback)
    {
        var configured = configuration["App:BaseUrl"];
        var candidate = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return null;
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    public static string Build(string baseUrl, string email, string token) =>
        new Uri(new Uri(baseUrl), "reset-password").AbsoluteUri +
        $"?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
}
