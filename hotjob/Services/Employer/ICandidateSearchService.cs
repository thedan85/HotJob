using hotjob.DTOs.Employer;
using hotjob.Services.Common;

namespace hotjob.Services.Employer;

/// <summary>
/// Employer-facing marketplace search over candidates who opted in to discovery,
/// plus the candidate-owned switch that controls discovery.
/// </summary>
public interface ICandidateSearchService
{
    /// <summary>
    /// Searches discoverable candidate profiles. The caller must be an Active member of an
    /// Approved company; results never include contact details or other private profile fields.
    /// Boosted profiles come first, then the newest profiles.
    /// </summary>
    Task<ServiceResult<PagedResult<CandidateSearchItemDto>>> SearchAsync(
        string? userId,
        CandidateSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Skills that appear on at least one discoverable profile, for the filter UI.</summary>
    Task<ServiceResult<IReadOnlyList<CandidateSearchSkillFilterDto>>> GetFilterSkillsAsync(
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Candidate-scoped discovery switch. Only the signed-in user's own candidate row can change;
    /// the returned value is the stored flag.
    /// </summary>
    Task<ServiceResult<bool>> SetDiscoverableAsync(
        string? userId,
        bool isDiscoverable,
        CancellationToken cancellationToken = default);
}
