using hotjob.Data;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Services.Billing;

/// <summary>Server-side Premium state of one candidate at the current time (contract D10).</summary>
public sealed record CandidatePremiumStatus(
    bool IsPremium,
    string? PlanName,
    DateTime? PremiumUntil,
    DateTime? BoostUntil);

/// <summary>Reads the candidate Premium gate used by premium-only features.</summary>
public interface ICandidatePremiumService
{
    /// <summary>True while <c>PremiumUntil</c> is in the future. False for guests and unknown users.</summary>
    Task<bool> IsPremiumAsync(string? userId, CancellationToken cancellationToken = default);

    /// <summary>Plan name and expiry for a candidate profile, or null when the user has no profile.</summary>
    Task<CandidatePremiumStatus?> GetStatusAsync(
        string? userId,
        CancellationToken cancellationToken = default);
}

/// <summary>Premium gate: membership is derived from the candidate row, never from client state.</summary>
public sealed class CandidatePremiumService(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : ICandidatePremiumService
{
    public async Task<bool> IsPremiumAsync(string? userId, CancellationToken cancellationToken = default) =>
        (await GetStatusAsync(userId, cancellationToken))?.IsPremium == true;

    public async Task<CandidatePremiumStatus?> GetStatusAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var candidate = await dbContext.Candidates
            .AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new
            {
                row.PremiumUntil,
                row.BoostUntil,
                PlanName = row.PremiumPlan != null ? row.PremiumPlan.Name : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new CandidatePremiumStatus(
            candidate.PremiumUntil is { } expiry && expiry > now,
            candidate.PlanName,
            candidate.PremiumUntil,
            candidate.BoostUntil);
    }
}
