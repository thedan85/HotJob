namespace hotjob.Models.Billing;

/// <summary>What a <see cref="Payment"/> buys; decides which entitlement is granted when it is confirmed.</summary>
public enum PaymentPurpose
{
    /// <summary>A company plan (Starter, Growth).</summary>
    EmployerPlan,

    /// <summary>A "featured" boost for one job posting.</summary>
    EmployerFeatured,

    /// <summary>A candidate Premium subscription period.</summary>
    CandidatePremium
}
