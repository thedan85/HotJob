using hotjob.Data;
using hotjob.Models;
using hotjob.Models.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace hotjob.Repositories.Employer;

/// <summary>EF Core implementation; ownership always scoped via Application.JobPosting.CompanyProfileId.</summary>
public sealed class ApplicantRepository(ApplicationDbContext db) : IApplicantRepository
{
    public Task<Models.Users.Employer?> GetEmployerByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        db.Employers.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<Application> Items, int TotalCount)> ListOwnedAsync(
        int companyProfileId,
        int? jobPostingId,
        ApplicationStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Applications
            .AsNoTracking()
            .Include(a => a.JobPosting)
            .Include(a => a.Candidate).ThenInclude(c => c.User)
            .Include(a => a.Resume)
            .Where(a => a.JobPosting.CompanyProfileId == companyProfileId);

        if (jobPostingId.HasValue)
            query = query.Where(a => a.JobPostingId == jobPostingId.Value);
        if (status.HasValue)
            query = query.Where(a => a.CurrentStatus == status.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.Like(a.Candidate.FullName, term) ||
                (a.Candidate.User.Email != null && EF.Functions.Like(a.Candidate.User.Email, term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.AppliedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Application?> GetOwnedDetailAsync(
        int companyProfileId,
        int applicationId,
        CancellationToken cancellationToken = default) =>
        db.Applications
            .AsNoTracking()
            .Include(a => a.JobPosting)
            .Include(a => a.Candidate).ThenInclude(c => c.User)
            .Include(a => a.Resume).ThenInclude(r => r.Educations)
            .Include(a => a.Resume).ThenInclude(r => r.WorkExperiences)
            .Include(a => a.Resume).ThenInclude(r => r.ResumeSkills).ThenInclude(rs => rs.Skill)
            .Include(a => a.StatusHistory)
            .Include(a => a.Interviews)
            .FirstOrDefaultAsync(
                a => a.Id == applicationId && a.JobPosting.CompanyProfileId == companyProfileId,
                cancellationToken);

    public Task<Application?> GetOwnedForUpdateAsync(
        int companyProfileId,
        int applicationId,
        CancellationToken cancellationToken = default) =>
        db.Applications
            .Include(a => a.JobPosting)
            .Include(a => a.Candidate)
            .FirstOrDefaultAsync(
                a => a.Id == applicationId && a.JobPosting.CompanyProfileId == companyProfileId,
                cancellationToken);

    public Task<bool> IsApplicationOwnedAsync(
        int companyProfileId,
        int applicationId,
        CancellationToken cancellationToken = default) =>
        db.Applications.AnyAsync(
            a => a.Id == applicationId && a.JobPosting.CompanyProfileId == companyProfileId,
            cancellationToken);

    public async Task<IReadOnlyList<Interview>> ListInterviewsAsync(
        int applicationId,
        CancellationToken cancellationToken = default) =>
        await db.Interviews
            .AsNoTracking()
            .Where(i => i.ApplicationId == applicationId)
            .OrderBy(i => i.InterviewDate)
            .ToListAsync(cancellationToken);

    public Task<Interview?> GetOwnedInterviewForUpdateAsync(
        int companyProfileId,
        int interviewId,
        CancellationToken cancellationToken = default) =>
        db.Interviews
            .Include(i => i.Application).ThenInclude(a => a.JobPosting)
            .Include(i => i.Application).ThenInclude(a => a.Candidate)
            .FirstOrDefaultAsync(
                i => i.Id == interviewId && i.Application.JobPosting.CompanyProfileId == companyProfileId,
                cancellationToken);

    public void AddHistory(ApplicationStatusHistory history) => db.ApplicationStatusHistories.Add(history);

    public void AddNotification(Notification notification) => db.Notifications.Add(notification);

    public void AddInterview(Interview interview) => db.Interviews.Add(interview);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        db.Database.BeginTransactionAsync(cancellationToken);
}
