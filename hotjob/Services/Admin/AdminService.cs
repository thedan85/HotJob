using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.Models.Job;
using hotjob.Models.Users;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Admin;

public sealed class AdminService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager) : IAdminService
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

    public async Task<ServiceResult<IReadOnlyList<AdminUserListItemDto>>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await dbContext.Users
            .Include(user => user.Candidate)
            .Include(user => user.Employer)
            .OrderByDescending(user => user.CreatedAt)
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

        return ServiceResult<IReadOnlyList<AdminUserListItemDto>>.Ok(users);
    }

    public async Task<ServiceResult<bool>> ToggleUserActiveAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<bool>.Fail(ServiceErrorType.Validation, "User id is required.");

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return ServiceResult<bool>.Fail(ServiceErrorType.NotFound, "User not found.");

        user.IsActive = !user.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<IReadOnlyList<AdminCompanyListItemDto>>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var companies = await dbContext.CompanyProfiles
            .AsNoTracking()
            .Include(company => company.Employers)
            .Include(company => company.VerifiedBy)
            .OrderByDescending(company => company.Id)
            .Select(company => new AdminCompanyListItemDto(
                company.Id,
                company.CompanyName,
                company.Status,
                company.Industry,
                company.Location,
                company.VerifiedAt,
                company.VerifiedBy != null ? company.VerifiedBy.Email : null,
                company.Employers.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<IReadOnlyList<AdminCompanyListItemDto>>.Ok(companies);
    }

    public async Task<ServiceResult<bool>> UpdateCompanyStatusAsync(int companyId, CompanyStatus status, string? rejectionReason, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            return ServiceResult<bool>.Fail(ServiceErrorType.Validation, "Company id is invalid.");

        var company = await dbContext.CompanyProfiles
            .FirstOrDefaultAsync(company => company.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
            return ServiceResult<bool>.Fail(ServiceErrorType.NotFound, "Company not found.");

        company.Status = status;
        company.RejectionReason = status == CompanyStatus.Rejected
            ? (string.IsNullOrWhiteSpace(rejectionReason) ? "Rejected by admin." : rejectionReason)
            : null;
        company.VerifiedAt = DateTime.UtcNow;
        company.VerifiedByAdminId = "admin";

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<IReadOnlyList<AdminJobListItemDto>>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await dbContext.JobPostings
            .AsNoTracking()
            .Include(job => job.Company)
            .Include(job => job.Applications)
            .OrderByDescending(job => job.CreatedAt)
            .Select(job => new AdminJobListItemDto(
                job.Id,
                job.Title,
                job.Company.CompanyName,
                job.Company.Location,
                job.Status,
                job.CreatedAt,
                job.Applications.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ServiceResult<IReadOnlyList<AdminJobListItemDto>>.Ok(jobs);
    }

    public async Task<ServiceResult<bool>> DeleteJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        if (jobId <= 0)
            return ServiceResult<bool>.Fail(ServiceErrorType.Validation, "Job id is invalid.");

        var job = await dbContext.JobPostings
            .FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken)
            .ConfigureAwait(false);
        if (job is null)
            return ServiceResult<bool>.Fail(ServiceErrorType.NotFound, "Job not found.");

        dbContext.JobPostings.Remove(job);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ServiceResult<bool>.Ok(true);
    }
}

