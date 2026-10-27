namespace hotjob.Models.Billing;

/// <summary>Lifecycle state of a <see cref="Payment"/>.</summary>
public enum PaymentStatus
{
    /// <summary>Created, waiting for the gateway result.</summary>
    Pending,

    /// <summary>Confirmed by the gateway (or a manual admin grant); entitlement was granted.</summary>
    Paid,

    /// <summary>Declined or cancelled at the gateway.</summary>
    Failed,

    /// <summary>Never completed within the payment window.</summary>
    Expired
}
