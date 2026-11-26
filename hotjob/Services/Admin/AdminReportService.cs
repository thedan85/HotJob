using hotjob.Data;
using hotjob.DTOs.Admin;
using hotjob.Models.Billing;
using hotjob.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Admin;

/// <summary>Read-only monthly statistics for the admin reports page.</summary>
public interface IAdminReportService
{
    /// <summary>
    /// The last <paramref name="months"/> calendar months (UTC), including the current one.
    /// Revenue counts Paid payments only, by <c>PaidAt</c>.
    /// </summary>
    Task<ServiceResult<AdminReportDto>> GetReportAsync(int months = 12, CancellationToken cancellationToken = default);
}

public sealed class AdminReportService(
    ApplicationDbContext dbContext,
    TimeProvider? timeProvider = null) : IAdminReportService
{
    public const int MaxMonths = 36;
    private const int TopCount = 10;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ServiceResult<AdminReportDto>> GetReportAsync(int months = 12, CancellationToken cancellationToken = default)
    {
        if (months is < 1 or > MaxMonths)
            return ServiceResult<AdminReportDto>.Fail(ServiceErrorType.Validation, $"Choose between 1 and {MaxMonths} months.");

        var now = clock.GetUtcNow().UtcDateTime;
        var to = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        var from = to.AddMonths(-months);

        // Counts are grouped by Year/Month in SQL (translatable on SQLite and MySQL).
        var applicationRows = await dbContext.Applications
            .Where(application => application.AppliedAt >= from && application.AppliedAt < to)
            .GroupBy(application => new { application.AppliedAt.Year, application.AppliedAt.Month })
            .Select(group => new MonthCount(group.Key.Year, group.Key.Month, group.Count()))
            .ToListAsync(cancellationToken);

        var jobRows = await dbContext.JobPostings
            .Where(job => job.CreatedAt >= from && job.CreatedAt < to)
            .GroupBy(job => new { job.CreatedAt.Year, job.CreatedAt.Month })
            .Select(group => new MonthCount(group.Key.Year, group.Key.Month, group.Count()))
            .ToListAsync(cancellationToken);

        var candidateRows = await NewUsersByMonthAsync(AppRoles.Candidate, from, to, cancellationToken);
        var employerRows = await NewUsersByMonthAsync(AppRoles.Employer, from, to, cancellationToken);

        // Summed in memory: SQLite cannot aggregate decimal columns.
        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.Status == PaymentStatus.Paid
                && payment.PaidAt != null && payment.PaidAt >= from && payment.PaidAt < to)
            .Select(payment => new { PaidAt = payment.PaidAt!.Value, payment.Amount, payment.Purpose })
            .ToListAsync(cancellationToken);

        var rows = new List<AdminMonthlyReportRow>(months);
        for (var month = from; month < to; month = month.AddMonths(1))
        {
            var (year, number) = (month.Year, month.Month);
            var monthPayments = payments.Where(p => p.PaidAt.Year == year && p.PaidAt.Month == number).ToList();
            var byPurpose = Enum.GetValues<PaymentPurpose>().ToDictionary(
                purpose => purpose.ToString(),
                purpose => monthPayments.Where(p => p.Purpose == purpose).Sum(p => p.Amount));

            rows.Add(new AdminMonthlyReportRow(
                year,
                number,
                CountFor(applicationRows, year, number),
                CountFor(candidateRows, year, number),
                CountFor(employerRows, year, number),
                CountFor(jobRows, year, number),
                monthPayments.Count,
                monthPayments.Sum(p => p.Amount),
                byPurpose));
        }

        var inRange = dbContext.Applications
            .Where(application => application.AppliedAt >= from && application.AppliedAt < to);

        var topCompanies = await inRange
            .GroupBy(application => new { application.JobPosting.CompanyProfileId, application.JobPosting.Company.CompanyName })
            .Select(group => new { group.Key.CompanyProfileId, group.Key.CompanyName, Count = group.Count() })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.CompanyName)
            .Take(TopCount)
            .ToListAsync(cancellationToken);

        var topJobs = await inRange
            .GroupBy(application => new { application.JobPostingId, application.JobPosting.Title, application.JobPosting.Company.CompanyName })
            .Select(group => new { group.Key.JobPostingId, group.Key.Title, group.Key.CompanyName, Count = group.Count() })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Title)
            .Take(TopCount)
            .ToListAsync(cancellationToken);

        return ServiceResult<AdminReportDto>.Ok(new AdminReportDto(
            from,
            to,
            rows,
            topCompanies.Select(row => new AdminTopItemDto(row.CompanyProfileId, row.CompanyName, null, row.Count)).ToList(),
            topJobs.Select(row => new AdminTopItemDto(row.JobPostingId, row.Title, row.CompanyName, row.Count)).ToList(),
            rows.Sum(row => row.Applications),
            rows.Sum(row => row.Revenue)));
    }

    private Task<List<MonthCount>> NewUsersByMonthAsync(string role, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        dbContext.Users
            .Where(user => user.CreatedAt >= from && user.CreatedAt < to
                && dbContext.UserRoles.Any(userRole => userRole.UserId == user.Id
                    && dbContext.Roles.Any(r => r.Id == userRole.RoleId && r.Name == role)))
            .GroupBy(user => new { user.CreatedAt.Year, user.CreatedAt.Month })
            .Select(group => new MonthCount(group.Key.Year, group.Key.Month, group.Count()))
            .ToListAsync(cancellationToken);

    private static int CountFor(IEnumerable<MonthCount> rows, int year, int month) =>
        rows.FirstOrDefault(row => row.Year == year && row.Month == month)?.Count ?? 0;

    private sealed record MonthCount(int Year, int Month, int Count);
}
