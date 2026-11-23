using System.ComponentModel.DataAnnotations;

namespace hotjob.DTOs.Admin;

/// <summary>Paginated list item for an employer account.</summary>
public record EmployerListItemDto(
    int Id,
    string UserId,
    string FullName,
    string? Position,
    bool IsOwner,
    string MembershipStatus,
    bool IsActive,
    DateTime? DeactivatedAt);

/// <summary>Detailed view of an employer account.</summary>
public record EmployerDetailDto(
    int Id,
    string UserId,
    string Email,
    string FullName,
    string? Position,
    bool IsOwner,
    string MembershipStatus,
    bool IsActive,
    DateTime JoinedAt,
    DateTime? DeactivatedAt,
    string? DeactivationReason,
    int CompanyId,
    string? CompanyName);

/// <summary>Body for rejecting or deactivating an employer. The admin is taken from the token.</summary>
public record RejectEmployerRequest(
    [Required, MinLength(1), MaxLength(500)] string Reason);
