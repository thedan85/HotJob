using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace hotjob.Services.Account.PasswordRecovery;

public sealed class PasswordResetRateLimiter(TimeProvider timeProvider, int maximumEntries = 10_000)
{
    private sealed record IpWindow(DateTimeOffset Expires, int Attempts);
    private readonly object sync = new();
    private readonly Dictionary<string, DateTimeOffset> emails = [];
    private readonly Dictionary<string, IpWindow> ips = [];
    private DateTimeOffset nextCleanup;

    public bool TryAcquire(string email, string ip)
    {
        var emailKey = Hash(email.Trim().ToUpperInvariant());
        var ipKey = Hash(NormalizeIp(ip));
        lock (sync)
        {
            var now = timeProvider.GetUtcNow();
            if (now >= nextCleanup || emails.Count >= maximumEntries || ips.Count >= maximumEntries)
            {
                foreach (var key in emails.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToArray())
                    emails.Remove(key);
                foreach (var key in ips.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToArray())
                    ips.Remove(key);
                nextCleanup = now.AddMinutes(1);
            }

            if (!ips.TryGetValue(ipKey, out var window) || window.Expires <= now)
            {
                if (!ips.ContainsKey(ipKey) && ips.Count >= maximumEntries) return false;
                window = new IpWindow(now.AddMinutes(10), 0);
            }
            if (window.Attempts >= 10) return false;
            // Charge IP attempts before checking the email cooldown.
            ips[ipKey] = window with { Attempts = window.Attempts + 1 };
            if (emails.TryGetValue(emailKey, out var cooldown) && cooldown > now) return false;
            if (!emails.ContainsKey(emailKey) && emails.Count >= maximumEntries) return false;
            emails[emailKey] = now.AddSeconds(60);
            return true;
        }
    }

    internal static string NormalizeIp(string? value) =>
        IPAddress.TryParse(value, out var address)
            ? (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString()
            : "unknown";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
