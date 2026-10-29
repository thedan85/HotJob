using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using hotjob.Models.Billing;
using Microsoft.Extensions.Options;

namespace hotjob.Services.Billing;

/// <summary>
/// VNPay 2.1.0 URL builder and callback verifier. The signature covers the ascending-sorted,
/// URL-encoded <c>vnp_*</c> parameters, excluding the hash itself and empty values (contract D14).
/// </summary>
public sealed class VnPayService(IOptions<VnPayOptions> options) : IVnPayService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly string[] ExcludedKeys = ["vnp_SecureHash", "vnp_SecureHashType"];

    private VnPayOptions Options => options.Value;

    public bool IsConfigured => Options.IsConfigured;

    public string CreatePaymentUrl(Payment payment, string clientIp, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Online payment is not configured.");
        }

        var createdAt = now.ToOffset(VietnamOffset);
        var expiresAt = createdAt.AddMinutes(15);
        var parameters = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = Options.Version,
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = Options.TmnCode,
            ["vnp_Locale"] = Options.Locale,
            ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = payment.VnpTxnRef,
            ["vnp_OrderInfo"] = $"HotJob {payment.Purpose} {payment.VnpTxnRef}",
            ["vnp_OrderType"] = "other",
            ["vnp_Amount"] = ToCents(payment.Amount).ToString(CultureInfo.InvariantCulture),
            ["vnp_ReturnUrl"] = Options.ReturnUrl,
            ["vnp_IpAddr"] = string.IsNullOrWhiteSpace(clientIp) ? "127.0.0.1" : clientIp,
            ["vnp_CreateDate"] = createdAt.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["vnp_ExpireDate"] = expiresAt.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        };

        var payload = BuildSignedPayload(parameters);
        var hash = ComputeHash(payload, Options.HashSecret);
        return $"{Options.BaseUrl}?{payload}&vnp_SecureHash={hash}";
    }

    public VnPayCallbackResult ValidateCallback(IReadOnlyDictionary<string, string?> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var providedHash = GetValue(query, "vnp_SecureHash");
        if (string.IsNullOrWhiteSpace(providedHash) || !IsConfigured)
        {
            return Parse(query, isValid: false);
        }

        var payload = BuildSignedPayload(query);
        var expectedHash = ComputeHash(payload, Options.HashSecret);
        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHash),
            Encoding.ASCII.GetBytes(providedHash.Trim().ToLowerInvariant()));

        return Parse(query, valid);
    }

    /// <summary>Builds the exact URL-encoded string that VNPay signs.</summary>
    public static string BuildSignedPayload(IReadOnlyDictionary<string, string?> parameters)
    {
        var pairs = parameters
            .Where(pair => pair.Key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase))
            .Where(pair => !ExcludedKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={WebUtility.UrlEncode(pair.Value)}");

        return string.Join("&", pairs);
    }

    /// <summary>Computes the lowercase hex HMAC-SHA512 of the signed payload.</summary>
    public static string ComputeHash(string payload, string hashSecret)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hashSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static VnPayCallbackResult Parse(IReadOnlyDictionary<string, string?> query, bool isValid) =>
        new(
            isValid,
            GetValue(query, "vnp_TxnRef"),
            GetValue(query, "vnp_ResponseCode"),
            GetValue(query, "vnp_TransactionStatus"),
            GetValue(query, "vnp_TransactionNo"),
            GetValue(query, "vnp_BankCode"),
            GetValue(query, "vnp_PayDate"),
            long.TryParse(GetValue(query, "vnp_Amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cents)
                ? cents
                : 0);

    private static string? GetValue(IReadOnlyDictionary<string, string?> query, string key) =>
        query.TryGetValue(key, out var value) ? value : null;

    /// <summary>Converts a VND amount to the integer VNPay expects (amount × 100).</summary>
    public static long ToCents(decimal amount) => checked((long)(amount * 100m));
}
