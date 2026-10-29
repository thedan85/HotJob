using System.ComponentModel.DataAnnotations;

namespace hotjob.Services.Billing;

/// <summary>Strongly-typed <c>Billing</c> configuration section.</summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Price of one featured-job boost, in VND.</summary>
    [Range(0, double.MaxValue)]
    public decimal FeaturedPrice { get; set; } = 200_000;

    /// <summary>Length of one featured-job boost, in days.</summary>
    [Range(1, 365)]
    public int FeaturedDays { get; set; } = 7;

    /// <summary>Lifetime of a VNPay checkout before it is marked expired.</summary>
    [Range(1, 120)]
    public int PaymentWindowMinutes { get; set; } = 15;
}
