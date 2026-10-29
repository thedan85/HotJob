using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Billing;

/// <summary>
/// One payment attempt or manual grant. Rows are never deleted by cascades: every foreign key is
/// Restrict or SetNull so billing history survives deleting a company, job or user.
/// </summary>
public class Payment
{
    public int Id { get; set; }

    /// <summary>Identity user who paid (or the admin for a manual grant).</summary>
    [Required, MaxLength(255)]
    public string PayerUserId { get; set; } = default!;

    /// <summary>Set when the purchase is for a company (Employer purposes).</summary>
    public int? CompanyProfileId { get; set; }

    /// <summary>Set when the purchase is for a candidate (CandidatePremium).</summary>
    public int? CandidateId { get; set; }

    public PaymentPurpose Purpose { get; set; }

    /// <summary>Plan bought, for plan purposes.</summary>
    public int? PlanId { get; set; }

    /// <summary>Job featured, for <see cref="PaymentPurpose.EmployerFeatured"/>. Becomes null if the job is deleted.</summary>
    public int? JobPostingId { get; set; }

    /// <summary>Amount in VND; stored server-side at creation and never taken from the client.</summary>
    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    /// <summary>
    /// Doubles as the optimistic-concurrency token (see ApplicationDbContext): a Pending to Paid claim
    /// issued through SaveChanges only succeeds for the first writer.
    /// </summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PaidAt { get; set; }

    /// <summary>Unique reference sent to VNPay as <c>vnp_TxnRef</c> (manual grants use a <c>MANUAL-</c> prefix).</summary>
    [Required, MaxLength(64)]
    public string VnpTxnRef { get; set; } = default!;

    [MaxLength(50)]
    public string? VnpTransactionNo { get; set; }

    [MaxLength(10)]
    public string? VnpResponseCode { get; set; }

    [MaxLength(20)]
    public string? VnpBankCode { get; set; }

    /// <summary>Raw <c>yyyyMMddHHmmss</c> timestamp reported by VNPay.</summary>
    [MaxLength(14)]
    public string? VnpPayDate { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public Users.ApplicationUser PayerUser { get; set; } = default!;

    public Users.CompanyProfile? Company { get; set; }

    public Users.Candidate? Candidate { get; set; }

    public Plan? Plan { get; set; }

    public Job.JobPosting? JobPosting { get; set; }
}
