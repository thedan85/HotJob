using hotjob.DTOs.Billing;
using hotjob.Services.Common;

namespace hotjob.Services.Billing;

/// <summary>Creates VNPay checkouts and applies confirmed payments to entitlements.</summary>
public interface IBillingService
{
    /// <summary>Creates a pending payment and signed checkout URL for an employer plan.</summary>
    Task<ServiceResult<CheckoutResponse>> CreateEmployerPlanCheckoutAsync(
        string userId,
        string planCode,
        string clientIp,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a pending payment and signed checkout URL for featuring one job.</summary>
    Task<ServiceResult<CheckoutResponse>> CreateFeaturedCheckoutAsync(
        string userId,
        int jobId,
        string clientIp,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a pending payment and signed checkout URL for candidate Premium.</summary>
    Task<ServiceResult<CheckoutResponse>> CreateCandidatePremiumCheckoutAsync(
        string userId,
        string planCode,
        string clientIp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies and applies one VNPay callback. Safe to call from both IPN and ReturnUrl:
    /// signature, amount and status are checked before anything changes, and the Pending → Paid
    /// claim is atomic so a replay grants nothing (contract D4/D5).
    /// </summary>
    Task<VnPayProcessResult> ProcessCallbackAsync(
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken = default);
}
