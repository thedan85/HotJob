using System.Globalization;
using hotjob.Data;
using hotjob.DTOs.Billing;
using hotjob.Models.Billing;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace hotjob.Services.Billing;

/// <summary>
/// VNPay checkout and callback processing. Money is always taken from the stored payment,
/// never from the callback, and the entitlement grant happens in the same SaveChanges as the
/// Pending → Paid claim (the Payment.Status concurrency token makes a replayed delivery lose).
/// </summary>
public sealed class BillingService(
    ApplicationDbContext dbContext,
    IVnPayService vnPay,
    IOptions<BillingOptions> billingOptions,
    TimeProvider timeProvider,
    ILogger<BillingService> logger) : IBillingService
{
    public async Task<ServiceResult<CheckoutResponse>> CreateEmployerPlanCheckoutAsync(
        string userId,
        string planCode,
        string clientIp,
        CancellationToken cancellationToken = default)
    {
        if (!vnPay.IsConfigured)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Validation, "Online payment is not configured.");
        }

        var employer = await GetOwnerEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Unauthorized,
                "Only the owner of an approved company can purchase a plan.");
        }

        var plan = await dbContext.Plans.FirstOrDefaultAsync(
            candidate => candidate.Code == planCode
                && candidate.Audience == PlanAudience.Employer
                && candidate.IsActive,
            cancellationToken);
        if (plan is null || plan.Price <= 0)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Validation, "This plan is not available for purchase.");
        }

        return await CreateCheckoutAsync(
            userId,
            employer.CompanyProfileId,
            candidateId: null,
            PaymentPurpose.EmployerPlan,
            plan,
            jobId: null,
            plan.Price,
            clientIp,
            cancellationToken);
    }

    public async Task<ServiceResult<CheckoutResponse>> CreateFeaturedCheckoutAsync(
        string userId,
        int jobId,
        string clientIp,
        CancellationToken cancellationToken = default)
    {
        if (!vnPay.IsConfigured)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Validation, "Online payment is not configured.");
        }

        var employer = await GetOwnerEmployerAsync(userId, cancellationToken);
        if (employer is null)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Unauthorized,
                "Only the owner of an approved company can purchase a featured job.");
        }

        var job = await dbContext.JobPostings.FirstOrDefaultAsync(
            posting => posting.Id == jobId && posting.CompanyProfileId == employer.CompanyProfileId,
            cancellationToken);
        if (job is null)
        {
            return ServiceResult<CheckoutResponse>.Fail(ServiceErrorType.NotFound, "Job not found.");
        }

        if (job.Status is not (JobStatus.Draft or JobStatus.Published))
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Validation, "Only active jobs can be featured.");
        }

        var price = billingOptions.Value.FeaturedPrice;
        if (price <= 0)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Validation, "Featured jobs are not available for purchase.");
        }

        return await CreateCheckoutAsync(
            userId,
            employer.CompanyProfileId,
            candidateId: null,
            PaymentPurpose.EmployerFeatured,
            plan: null,
            job.Id,
            price,
            clientIp,
            cancellationToken);
    }

    public async Task<ServiceResult<CheckoutResponse>> CreateCandidatePremiumCheckoutAsync(
        string userId,
        string planCode,
        string clientIp,
        CancellationToken cancellationToken = default)
    {
        if (!vnPay.IsConfigured)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Validation, "Online payment is not configured.");
        }

        var candidate = await dbContext.Candidates.FirstOrDefaultAsync(
            profile => profile.UserId == userId, cancellationToken);
        if (candidate is null)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Unauthorized, "A candidate profile is required.");
        }

        var plan = await dbContext.Plans.FirstOrDefaultAsync(
            candidatePlan => candidatePlan.Code == planCode
                && candidatePlan.Audience == PlanAudience.Candidate
                && candidatePlan.IsActive,
            cancellationToken);
        if (plan is null || plan.Price <= 0)
        {
            return ServiceResult<CheckoutResponse>.Fail(
                ServiceErrorType.Validation, "This plan is not available for purchase.");
        }

        return await CreateCheckoutAsync(
            userId,
            companyProfileId: null,
            candidate.Id,
            PaymentPurpose.CandidatePremium,
            plan,
            jobId: null,
            plan.Price,
            clientIp,
            cancellationToken);
    }

    public async Task<VnPayProcessResult> ProcessCallbackAsync(
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken = default)
    {
        var callback = vnPay.ValidateCallback(query);
        if (!callback.IsValidSignature)
        {
            logger.LogWarning("Rejected VNPay callback with an invalid signature.");
            return new VnPayProcessResult("97", "Invalid signature", Granted: false);
        }

        if (string.IsNullOrWhiteSpace(callback.TxnRef))
        {
            return new VnPayProcessResult("01", "Transaction not found", Granted: false);
        }

        var payment = await dbContext.Payments
            .Include(row => row.Plan)
            .FirstOrDefaultAsync(row => row.VnpTxnRef == callback.TxnRef, cancellationToken);
        if (payment is null)
        {
            return new VnPayProcessResult("01", "Transaction not found", Granted: false);
        }

        var purpose = payment.Purpose.ToString();
        var amount = payment.Amount;
        if (callback.AmountInCents != VnPayService.ToCents(amount))
        {
            logger.LogWarning(
                "VNPay amount mismatch for {TxnRef}: callback {CallbackAmount} vs stored {StoredAmount}.",
                payment.VnpTxnRef,
                callback.AmountInCents,
                VnPayService.ToCents(amount));
            return new VnPayProcessResult(
                "04", "Invalid amount", Granted: false, purpose, amount, payment.VnpTxnRef);
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            return new VnPayProcessResult(
                "02", "Transaction already confirmed", Granted: false, purpose, amount, payment.VnpTxnRef);
        }

        var now = timeProvider.GetUtcNow();
        if (now.UtcDateTime - payment.CreatedAt > TimeSpan.FromMinutes(billingOptions.Value.PaymentWindowMinutes))
        {
            payment.Status = PaymentStatus.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new VnPayProcessResult(
                "00", "Payment expired", Granted: false, purpose, amount, payment.VnpTxnRef);
        }

        RecordGatewayResult(payment, callback);
        if (callback.ResponseCode != "00" || callback.TransactionStatus != "00")
        {
            payment.Status = PaymentStatus.Failed;
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "VNPay payment {TxnRef} failed with response {ResponseCode}/{TransactionStatus}.",
                payment.VnpTxnRef,
                callback.ResponseCode,
                callback.TransactionStatus);
            return new VnPayProcessResult(
                "00", "Payment failed", Granted: false, purpose, amount, payment.VnpTxnRef);
        }

        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = now.UtcDateTime;
        await GrantAsync(payment, now.UtcDateTime, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent IPN/Return delivery claimed the row first; nothing was granted.
            dbContext.ChangeTracker.Clear();
            return new VnPayProcessResult(
                "02", "Transaction already confirmed", Granted: false, purpose, amount, payment.VnpTxnRef);
        }

        logger.LogInformation(
            "VNPay payment {TxnRef} confirmed for {Purpose}; granted entitlement.",
            payment.VnpTxnRef,
            payment.Purpose);
        return new VnPayProcessResult(
            "00", "Confirm Success", Granted: true, purpose, amount, payment.VnpTxnRef);
    }

    private async Task<ServiceResult<CheckoutResponse>> CreateCheckoutAsync(
        string userId,
        int? companyProfileId,
        int? candidateId,
        PaymentPurpose purpose,
        Plan? plan,
        int? jobId,
        decimal amount,
        string clientIp,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var payment = new Payment
        {
            PayerUserId = userId,
            CompanyProfileId = companyProfileId,
            CandidateId = candidateId,
            Purpose = purpose,
            PlanId = plan?.Id,
            JobPostingId = jobId,
            Amount = amount,
            Status = PaymentStatus.Pending,
            CreatedAt = now.UtcDateTime,
            VnpTxnRef = NewTxnRef(now)
        };

        dbContext.Payments.Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken);

        var paymentUrl = vnPay.CreatePaymentUrl(payment, clientIp, now);
        return ServiceResult<CheckoutResponse>.Ok(new CheckoutResponse(
            payment.Id,
            payment.VnpTxnRef,
            payment.Amount,
            payment.Purpose.ToString(),
            paymentUrl));
    }

    private async Task<Models.Users.Employer?> GetOwnerEmployerAsync(string userId, CancellationToken cancellationToken) =>
        await dbContext.Employers
            .Include(member => member.Company)
            .FirstOrDefaultAsync(
                member => member.UserId == userId
                    && member.IsOwner
                    && member.MembershipStatus == MembershipStatus.Active
                    && member.Company.Status == CompanyStatus.Approved,
                cancellationToken);

    private async Task GrantAsync(Payment payment, DateTime now, CancellationToken cancellationToken)
    {
        switch (payment.Purpose)
        {
            case PaymentPurpose.EmployerPlan:
                await GrantEmployerPlanAsync(payment, now, cancellationToken);
                break;
            case PaymentPurpose.EmployerFeatured:
                await GrantFeaturedAsync(payment, now, cancellationToken);
                break;
            case PaymentPurpose.CandidatePremium:
                await GrantCandidatePremiumAsync(payment, now, cancellationToken);
                break;
        }
    }

    private async Task GrantEmployerPlanAsync(
        Payment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (payment.CompanyProfileId is not { } companyId || payment.Plan is not { } plan)
        {
            logger.LogError("Payment {TxnRef} cannot grant an employer plan: missing company or plan.", payment.VnpTxnRef);
            return;
        }

        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(profile => profile.Id == companyId, cancellationToken);
        if (company is null)
        {
            logger.LogError("Payment {TxnRef} cannot grant a plan: company {CompanyId} is gone.", payment.VnpTxnRef, companyId);
            return;
        }

        var samePlanActive = company.PlanId == plan.Id
            && company.PlanExpiresAt is { } expiry
            && expiry > now;
        var startsAt = samePlanActive && company.PlanExpiresAt is { } currentExpiry ? currentExpiry : now;

        company.PlanId = plan.Id;
        if (!samePlanActive)
        {
            company.PlanStartedAt = now;
        }

        company.PlanExpiresAt = plan.DurationDays <= 0 ? null : startsAt.AddDays(plan.DurationDays);
    }

    private async Task GrantFeaturedAsync(
        Payment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (payment.JobPostingId is not { } jobId)
        {
            logger.LogWarning("Payment {TxnRef} has no job to feature (deleted after checkout).", payment.VnpTxnRef);
            return;
        }

        var job = await dbContext.JobPostings
            .FirstOrDefaultAsync(posting => posting.Id == jobId, cancellationToken);
        if (job is null)
        {
            logger.LogWarning("Payment {TxnRef} cannot feature job {JobId}: it no longer exists.", payment.VnpTxnRef, jobId);
            return;
        }

        var startsAt = job.FeaturedUntil is { } featuredUntil && featuredUntil > now ? featuredUntil : now;
        job.FeaturedUntil = startsAt.AddDays(billingOptions.Value.FeaturedDays);
    }

    private async Task GrantCandidatePremiumAsync(
        Payment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (payment.CandidateId is not { } candidateId || payment.Plan is not { } plan)
        {
            logger.LogError("Payment {TxnRef} cannot grant Premium: missing candidate or plan.", payment.VnpTxnRef);
            return;
        }

        var candidate = await dbContext.Candidates
            .FirstOrDefaultAsync(profile => profile.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            logger.LogError("Payment {TxnRef} cannot grant Premium: candidate {CandidateId} is gone.", payment.VnpTxnRef, candidateId);
            return;
        }

        var active = candidate.PremiumUntil is { } until && until > now;
        var startsAt = active ? candidate.PremiumUntil!.Value : now;
        if (!active)
        {
            candidate.PremiumStartedAt = now;
        }

        candidate.PremiumPlanId = plan.Id;
        candidate.PremiumUntil = startsAt.AddDays(plan.DurationDays);
        candidate.BoostUntil = candidate.PremiumUntil;
    }

    private static void RecordGatewayResult(Payment payment, VnPayCallbackResult callback)
    {
        payment.VnpTransactionNo = Truncate(callback.TransactionNo, 50);
        payment.VnpResponseCode = Truncate(callback.ResponseCode, 10);
        payment.VnpBankCode = Truncate(callback.BankCode, 20);
        payment.VnpPayDate = Truncate(callback.PayDate, 14);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];

    private static string NewTxnRef(DateTimeOffset now) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"HJ{now.UtcDateTime:yyMMddHHmmss}{Guid.NewGuid():N}")[..24];
}
