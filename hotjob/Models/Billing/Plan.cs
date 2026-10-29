using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Billing;

/// <summary>A purchasable (or free) plan from the catalogue. Seeded by <see cref="Plan.Code"/>.</summary>
public class Plan
{
    /// <summary>Code of the perpetual free employer plan.</summary>
    public const string FreeCode = "FREE";

    /// <summary>Code of the 30-day Starter employer plan.</summary>
    public const string StarterCode = "STARTER";

    /// <summary>Code of the 30-day Growth employer plan.</summary>
    public const string GrowthCode = "GROWTH";

    /// <summary>Code of the 30-day candidate Premium plan.</summary>
    public const string PremiumMonthlyCode = "PREMIUM_MONTHLY";

    public int Id { get; set; }

    public PlanAudience Audience { get; set; }

    /// <summary>Stable unique identifier such as <c>STARTER</c>; code looks plans up by this, never by Id.</summary>
    [Required, MaxLength(30)]
    public string Code { get; set; } = default!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = default!;

    /// <summary>Price in VND (no fractional part).</summary>
    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    /// <summary>Concurrent active (Draft/Published) job slots. Only meaningful for Employer plans.</summary>
    [Range(0, int.MaxValue)]
    public int JobQuota { get; set; }

    /// <summary>Length of one purchase in days; 0 means perpetual.</summary>
    [Range(0, int.MaxValue)]
    public int DurationDays { get; set; }

    /// <summary>Whether the plan can currently be bought.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
