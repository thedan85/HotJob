using hotjob.Models.Billing;

namespace hotjob.Services.Billing;

/// <summary>Parsed and signature-checked VNPay callback parameters.</summary>
public sealed record VnPayCallbackResult(
    bool IsValidSignature,
    string? TxnRef,
    string? ResponseCode,
    string? TransactionStatus,
    string? TransactionNo,
    string? BankCode,
    string? PayDate,
    long AmountInCents);

/// <summary>Builds VNPay payment URLs and verifies callback signatures.</summary>
public interface IVnPayService
{
    /// <summary>Whether a merchant code and hash secret are configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Builds the signed sandbox/production payment URL for a pending payment.</summary>
    string CreatePaymentUrl(Payment payment, string clientIp, DateTimeOffset now);

    /// <summary>Verifies the HMAC-SHA512 signature and parses the callback parameters.</summary>
    VnPayCallbackResult ValidateCallback(IReadOnlyDictionary<string, string?> query);
}
