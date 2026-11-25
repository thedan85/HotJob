using System.Data.Common;
using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.DTOs.Job;
using hotjob.Models;
using hotjob.Models.Admin;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace hotjob.Services.Admin;

public sealed class AdminService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IAdminAuditService auditService) : IAdminService
{
    public async Task<ServiceResult<AdminDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var totalUsers = await dbContext.Users.CountAsync(cancellationToken).ConfigureAwait(false);
        var activeUsers = await dbContext.Users.CountAsync(user => user.IsActive, cancellationToken).ConfigureAwait(false);

        var adminRoleId = await dbContext.Roles
            .Where(role => role.Name == AppRoles.Admin)
            .Select(role => role.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var candidateRoleId = await dbContext.Roles
            .Where(role => role.Name == AppRoles.Candidate)
            .Select(role => role.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var employerRoleId = await dbContext.Roles
            .Where(role => role.Name == AppRoles.Employer)
            .Select(role => role.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var adminUsers = adminRoleId is null || adminRoleId == string.Empty
            ? 0
            : await dbContext.UserRoles
                .CountAsync(userRole => userRole.RoleId == adminRoleId && dbContext.Users.Any(user => user.Id == userRole.UserId && user.IsActive), cancellationToken)
                .ConfigureAwait(false);

        var candidateUsers = candidateRoleId is null || candidateRoleId == string.Empty
            ? 0
            : await dbContext.UserRoles
                .CountAsync(userRole => userRole.RoleId == candidateRoleId && dbContext.Users.Any(user => user.Id == userRole.UserId && user.IsActive), cancellationToken)
                .ConfigureAwait(false);

        var employerUsers = employerRoleId is null || employerRoleId == string.Empty
            ? 0
            : await dbContext.UserRoles
                .CountAsync(userRole => userRole.RoleId == employerRoleId && dbContext.Users.Any(user => user.Id == userRole.UserId && user.IsActive), cancellationToken)
                .ConfigureAwait(false);

        var totalCompanies = await dbContext.CompanyProfiles.CountAsync(cancellationToken).ConfigureAwait(false);
        var pendingCompanies = await dbContext.CompanyProfiles.CountAsync(company => company.Status == CompanyStatus.Pending, cancellationToken).ConfigureAwait(false);
        var approvedCompanies = await dbContext.CompanyProfiles.CountAsync(company => company.Status == CompanyStatus.Approved, cancellationToken).ConfigureAwait(false);
        var rejectedCompanies = await dbContext.CompanyProfiles.CountAsync(company => company.Status == CompanyStatus.Rejected, cancellationToken).ConfigureAwait(false);

        var totalJobs = await dbContext.JobPostings.CountAsync(cancellationToken).ConfigureAwait(false);
        var publishedJobs = await dbContext.JobPostings.CountAsync(job => job.Status == JobStatus.Published, cancellationToken).ConfigureAwait(false);
        var draftJobs = await dbContext.JobPostings.CountAsync(job => job.Status == JobStatus.Draft, cancellationToken).ConfigureAwait(false);
        var closedJobs = await dbContext.JobPostings.CountAsync(job => job.Status == JobStatus.Closed, cancellationToken).ConfigureAwait(false);
        var expiredJobs = await dbContext.JobPostings.CountAsync(job => job.Status == JobStatus.Expired, cancellationToken).ConfigureAwait(false);

        return ServiceResult<AdminDashboardDto>.Ok(new AdminDashboardDto(
            totalUsers,
            activeUsers,
            adminUsers,
            candidateUsers,
            employerUsers,
            totalCompanies,
            pendingCompanies,
            approvedCompanies,
            rejectedCompanies,
            totalJobs,
            publishedJobs,
            draftJobs,
            closedJobs,
            expiredJobs));
    }

    public async Task<ServiceResult<PagedResponse<AdminUserListItemDto>>> GetUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);

        var query = dbContext.Users
            .AsNoTracking()
            .OrderByDescending(user => user.CreatedAt)
            .ThenByDescending(user => user.Id);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
        {
            return ServiceResult<PagedResponse<AdminUserListItemDto>>.Ok(
                new PagedResponse<AdminUserListItemDto> { TotalCount = total, Page = page, PageSize = pageSize });
        }

        var users = await query
            .Skip((int)offset)
            .Take(pageSize)
            .Select(user => new AdminUserListItemDto(
                user.Id,
                user.Email ?? string.Empty,
                user.Candidate != null ? user.Candidate.FullName : user.Employer != null ? user.Employer.FullName : null,
                user.IsActive,
                dbContext.UserRoles
                    .Where(ur => ur.UserId == user.Id)
                    .Join(dbContext.Roles, ur => ur.RoleId, role => role.Id, (ur, role) => role.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList(),
                user.CreatedAt,
                user.Candidate != null ? "Candidate" : user.Employer != null ? "Employer" : "User"))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<PagedResponse<AdminUserListItemDto>>.Ok(new PagedResponse<AdminUserListItemDto>
        {
            Items = users,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult<bool>> ToggleUserActiveAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureNoTransaction();
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<bool>.Fail(ServiceErrorType.Validation, "User id is required.");

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return ServiceResult<bool>.Fail(ServiceErrorType.NotFound, "User not found.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var committed = false;
        try
        {
        user.IsActive = !user.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditService.LogAsync(
            user.IsActive ? AuditActions.UserUnlock : AuditActions.UserLock,
            AuditTargetTypes.User,
            user.Id,
            user.IsActive
                ? $"Enabled account for {user.Email}."
                : $"Disabled account for {user.Email}.").ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        committed = true;
        return ServiceResult<bool>.Ok(true);
        }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { dbContext.ChangeTracker.Clear(); }
            }
        }
    }

    public async Task<ServiceResult<bool>> DeleteJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        EnsureNoTransaction();
        if (jobId <= 0)
            return ServiceResult<bool>.Fail(ServiceErrorType.Validation, "Job id is invalid.");

        var job = await dbContext.JobPostings
            .Include(job => job.Company)
            .FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken)
            .ConfigureAwait(false);
        if (job is null)
            return ServiceResult<bool>.Fail(ServiceErrorType.NotFound, "Job not found.");

        var jobTitle = job.Title;
        var companyName = job.Company.CompanyName;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var committed = false;
        try
        {
        dbContext.JobPostings.Remove(job);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditService.LogAsync(
            AuditActions.JobDelete,
            AuditTargetTypes.Job,
            jobId.ToString(),
            $"Deleted job '{jobTitle}' at '{companyName}'.").ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        committed = true;
        return ServiceResult<bool>.Ok(true);
        }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { dbContext.ChangeTracker.Clear(); }
            }
        }
    }

    public async Task<ServiceResult<IReadOnlyList<AdminTrainingLogDto>>> GetTrainingLogsAsync(
        CancellationToken cancellationToken = default)
    {
        var logs = await dbContext.ModelTrainingLogs
            .AsNoTracking()
            .Include(log => log.TrainedBy)
            .OrderByDescending(log => log.TrainedAt)
            .ThenByDescending(log => log.Id)
            .Select(log => new AdminTrainingLogDto(
                log.Id,
                log.RecordCount,
                log.AccuracyScore,
                log.ModelFilePath,
                log.TrainedBy != null ? log.TrainedBy.Email : null,
                log.TrainedAt,
                log.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<IReadOnlyList<AdminTrainingLogDto>>.Ok(logs);
    }

    public async Task<ServiceResult<PagedResponse<AdminSkillDto>>> GetSkillsAsync(
        string? search,
        string? category,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = dbContext.Skills.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(skill => EF.Functions.Like(skill.Name, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var term = category.Trim();
            query = query.Where(skill => skill.Category == term);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
        {
            return ServiceResult<PagedResponse<AdminSkillDto>>.Ok(
                new PagedResponse<AdminSkillDto>
                {
                    TotalCount = total,
                    Page = page,
                    PageSize = pageSize
                });
        }

        var items = await query
            .OrderBy(skill => skill.Name)
            .ThenBy(skill => skill.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(skill => new AdminSkillDto(
                skill.Id,
                skill.Name,
                skill.Category,
                skill.ResumeSkills.Count,
                skill.JobSkills.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<PagedResponse<AdminSkillDto>>.Ok(
            new PagedResponse<AdminSkillDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            });
    }

    public async Task<ServiceResult<AdminSkillDto>> GetSkillAsync(
        int skillId,
        CancellationToken cancellationToken = default)
    {
        var dto = await dbContext.Skills
            .AsNoTracking()
            .Where(skill => skill.Id == skillId)
            .Select(skill => new AdminSkillDto(
                skill.Id,
                skill.Name,
                skill.Category,
                skill.ResumeSkills.Count,
                skill.JobSkills.Count))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.NotFound, "Skill not found.")
            : ServiceResult<AdminSkillDto>.Ok(dto);
    }

    public async Task<ServiceResult<AdminSkillDto>> CreateSkillAsync(
        CreateSkillRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureNoTransaction();
        if (request is null)
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Validation, "Skill data is required.");

        var (name, category, errors) = NormalizeSkillInput(request.Name, request.Category);
        if (errors.Length > 0)
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Validation, errors);

        var normalizedName = name.ToLower();
        var exists = await dbContext.Skills
            .AnyAsync(skill => skill.Name.ToLower() == normalizedName, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Conflict, "Skill name already exists.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var committed = false;
        try
        {
        var entity = new Skill { Name = name, Category = category };
        dbContext.Skills.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsSkillNameConflict(exception))
        {
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Conflict, "Skill name already exists.");
        }

        await auditService.LogAsync(
            AuditActions.SkillCreate,
            AuditTargetTypes.Skill,
            entity.Id.ToString(),
            $"Created skill '{entity.Name}'.").ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        committed = true;
        return ServiceResult<AdminSkillDto>.Ok(
            new AdminSkillDto(entity.Id, entity.Name, entity.Category, 0, 0));
        }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { dbContext.ChangeTracker.Clear(); }
            }
        }
    }

    public async Task<ServiceResult<AdminSkillDto>> UpdateSkillAsync(
        int skillId,
        UpdateSkillRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureNoTransaction();
        var entity = await dbContext.Skills
            .FirstOrDefaultAsync(skill => skill.Id == skillId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.NotFound, "Skill not found.");

        if (request is null)
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Validation, "Skill data is required.");

        var (name, category, errors) = NormalizeSkillInput(request.Name, request.Category);
        if (errors.Length > 0)
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Validation, errors);

        var normalizedName = name.ToLower();
        var exists = await dbContext.Skills
            .AnyAsync(
                skill => skill.Id != skillId && skill.Name.ToLower() == normalizedName,
                cancellationToken)
            .ConfigureAwait(false);

        if (exists)
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Conflict, "Skill name already exists.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var committed = false;
        try
        {
        entity.Name = name;
        entity.Category = category;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsSkillNameConflict(exception))
        {
            return ServiceResult<AdminSkillDto>.Fail(ServiceErrorType.Conflict, "Skill name already exists.");
        }

        await auditService.LogAsync(
            AuditActions.SkillUpdate,
            AuditTargetTypes.Skill,
            skillId.ToString(),
            $"Updated skill '{entity.Name}'.").ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        committed = true;
        var dto = await dbContext.Skills
            .AsNoTracking()
            .Where(skill => skill.Id == skillId)
            .Select(skill => new AdminSkillDto(
                skill.Id,
                skill.Name,
                skill.Category,
                skill.ResumeSkills.Count,
                skill.JobSkills.Count))
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<AdminSkillDto>.Ok(dto);
        }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { dbContext.ChangeTracker.Clear(); }
            }
        }
    }

    public async Task<ServiceResult> DeleteSkillAsync(
        int skillId,
        CancellationToken cancellationToken = default)
    {
        EnsureNoTransaction();
        var entity = await dbContext.Skills
            .FirstOrDefaultAsync(skill => skill.Id == skillId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
            return ServiceResult.Fail(ServiceErrorType.NotFound, "Skill not found.");

        var resumeCount = await dbContext.ResumeSkills
            .CountAsync(link => link.SkillId == skillId, cancellationToken)
            .ConfigureAwait(false);

        var jobCount = await dbContext.JobSkills
            .CountAsync(link => link.SkillId == skillId, cancellationToken)
            .ConfigureAwait(false);

        if (resumeCount > 0 || jobCount > 0)
        {
            return ServiceResult.Fail(
                ServiceErrorType.Conflict,
                $"Cannot delete skill '{entity.Name}': used in {resumeCount} resumes and {jobCount} job postings.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var committed = false;
        try
        {
        dbContext.Skills.Remove(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsSkillReferenceConflict(exception))
        {
            return ServiceResult.Fail(ServiceErrorType.Conflict, "Cannot delete skill: it is in use.");
        }

        await auditService.LogAsync(
            AuditActions.SkillDelete,
            AuditTargetTypes.Skill,
            skillId.ToString(),
            $"Deleted skill '{entity.Name}'.").ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        committed = true;
        return ServiceResult.Ok();
        }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { dbContext.ChangeTracker.Clear(); }
            }
        }
    }

    private void EnsureNoTransaction()
    {
        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Admin write operations require ownership of the database transaction.");
    }

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, 200));

    // Classify only expected constraints; unrelated storage and concurrency errors must propagate.
    // MySQL codes: https://dev.mysql.com/doc/mysql-errors/8.0/en/server-error-reference.html
    private static bool IsSkillNameConflict(DbUpdateException exception) =>
        exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } ||
        exception.InnerException is DbException databaseException &&
        databaseException.Message.Contains("UNIQUE constraint failed: Skills.Name", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkillReferenceConflict(DbUpdateException exception) =>
        exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.RowIsReferenced2 } ||
        exception.InnerException is DbException databaseException &&
        databaseException.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase);

    private static (string Name, string? Category, string[] Errors) NormalizeSkillInput(
        string? name,
        string? category)
    {
        var errors = new List<string>();
        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        if (normalizedName.Length == 0)
            errors.Add("Skill name is required.");

        if (normalizedName.Length > 100)
            errors.Add("Skill name must be 100 characters or fewer.");

        if (normalizedCategory is { Length: > 100 })
            errors.Add("Skill category must be 100 characters or fewer.");

        return (normalizedName, normalizedCategory, errors.ToArray());
    }
}

