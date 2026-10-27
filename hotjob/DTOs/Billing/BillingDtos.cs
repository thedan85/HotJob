namespace hotjob.DTOs.Billing;

/// <summary>Result of creating a payment attempt. <see cref="PaymentUrl"/> is the signed gateway URL.</summary>
public sealed record CheckoutResponse(
    int PaymentId,
    string TxnRef,
    decimal Amount,
    string Purpose,
    string PaymentUrl);

/// <summary>Outcome of processing a VNPay IPN or return callback.</summary>
public sealed record VnPayProcessResult(
    string RspCode,
    string Message,
    bool Granted,
    string? Purpose = null,
    decimal? Amount = null,
    string? TxnRef = null);
