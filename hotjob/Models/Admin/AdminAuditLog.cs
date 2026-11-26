using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Admin;

/// <summary>Auditable admin operations. Stored in <see cref="AdminAuditLog.Action"/>.</summary>
public static class AuditActions
{
    public const string CompanyApprove = "Company.Approve";
    public const string CompanyReject = "Company.Reject";
    public const string CompanyDeactivate = "Company.Deactivate";
    public const string CompanyReactivate = "Company.Reactivate";
    public const string EmployerApprove = "Employer.Approve";
    public const string EmployerReject = "Employer.Reject";
    public const string EmployerDeactivate = "Employer.Deactivate";
    public const string EmployerReactivate = "Employer.Reactivate";
    public const string JobApprove = "Job.Approve";
    public const string JobReject = "Job.Reject";
    public const string JobRemove = "Job.Remove";
    public const string ReviewHide = "Review.Hide";
    public const string ReviewReportsDismiss = "Review.ReportsDismiss";
    public const string UserLock = "User.Lock";
    public const string UserUnlock = "User.Unlock";
    public const string JobDelete = "Job.Delete";
    public const string SkillCreate = "Skill.Create";
    public const string SkillUpdate = "Skill.Update";
    public const string SkillDelete = "Skill.Delete";

    /// <summary>All known actions, for filter menus.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CompanyApprove,
        CompanyReject,
        CompanyDeactivate,
        CompanyReactivate,
        EmployerApprove,
        EmployerReject,
        EmployerDeactivate,
        EmployerReactivate,
        JobApprove,
        JobReject,
        JobRemove,
        ReviewHide,
        ReviewReportsDismiss,
        UserLock,
        UserUnlock,
        JobDelete,
        SkillCreate,
        SkillUpdate,
        SkillDelete
    ];
}

/// <summary>Target entity kinds. Stored in <see cref="AdminAuditLog.TargetType"/>.</summary>
public static class AuditTargetTypes
{
    public const string Company = "Company";
    public const string User = "User";
    public const string Job = "Job";
    public const string Skill = "Skill";
    public const string Employer = "Employer";
    public const string Review = "Review";

    /// <summary>All known target types, for filter menus.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Company,
        User,
        Job,
        Skill,
        Employer,
        Review
    ];
}

/// <summary>Append-only record of an admin action, kept after the target is changed or deleted.</summary>
public class AdminAuditLog
{
    public int Id { get; set; }

    /// <summary>Id of the admin who performed the action.</summary>
    [Required, MaxLength(255)]
    public string AdminUserId { get; set; } = default!;

    /// <summary>Email snapshot so the trail survives account changes.</summary>
    [Required, MaxLength(255)]
    public string AdminEmail { get; set; } = default!;

    /// <summary>Operation name, one of <see cref="AuditActions"/>.</summary>
    [Required, MaxLength(100)]
    public string Action { get; set; } = default!;

    /// <summary>Entity kind, one of <see cref="AuditTargetTypes"/>.</summary>
    [Required, MaxLength(50)]
    public string TargetType { get; set; } = default!;

    /// <summary>Identifier of the affected entity.</summary>
    [Required, MaxLength(100)]
    public string TargetId { get; set; } = default!;

    /// <summary>Short human-readable context. Never contains passwords, tokens, or secrets.</summary>
    [MaxLength(500)]
    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
