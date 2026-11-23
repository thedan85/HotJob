using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Services.Common;

namespace hotjob.Services.Admin;

/// <summary>Writes and queries the admin audit trail.</summary>
public interface IAdminAuditService
{
    /// <summary>
    /// Appends an entry for the signed-in admin; throws if the admin ID or email is unavailable.
    /// </summary>
    Task LogAsync(string action, string targetType, string targetId, string? detail = null);

    /// <summary>Returns audit entries newest first, with optional exact and date filters.</summary>
    Task<ServiceResult<PagedResponse<AdminAuditLogDto>>> GetLogsAsync(
        int page,
        int pageSize,
        string? action = null,
        string? targetType = null,
        string? adminEmail = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);
}
