using hotjob.DTOs.ML;
using hotjob.Services.Common;

namespace hotjob.Services.ML;

/// <summary>Scores resumes against job postings and trains the FastTree model (contract D9).</summary>
public interface IMatchScoreService
{
    /// <summary>
    /// Returns the basic percentage for one of the caller's resumes (primary when
    /// <paramref name="resumeId"/> is null), recomputing and upserting it when stale.
    /// </summary>
    Task<ServiceResult<MatchScoreDto>> GetAsync(
        string? userId,
        int jobId,
        int? resumeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Premium-only breakdown. Free candidates get an unauthorized result with no
    /// breakdown data, regardless of what the client asks for.
    /// </summary>
    Task<ServiceResult<AdvancedMatchScoreDto>> GetAdvancedAsync(
        string? userId,
        int jobId,
        int resumeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trains from finished applications and promotes the model only when its
    /// hold-out accuracy is at least the active model's. Safe to call with too
    /// little data: it reports "insufficient data" and keeps the weighted rule.
    /// </summary>
    Task<ServiceResult<MatchTrainingResultDto>> TrainAsync(
        string? trainedByUserId,
        CancellationToken cancellationToken = default);
}
