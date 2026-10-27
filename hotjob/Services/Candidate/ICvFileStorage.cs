using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

/// <summary>Stores candidate CV files below a single uploads root and returns their public URL.</summary>
public interface ICvFileStorage
{
    /// <summary>Validates and stores one CV file for the candidate.</summary>
    /// <param name="candidateId">Owner of the file; also the sub-directory name.</param>
    /// <param name="fileName">Original browser file name, used only for validation and extension.</param>
    /// <param name="length">Reported file length in bytes.</param>
    /// <param name="content">File content stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The relative URL to store on <c>Resume.FileUrl</c>, or a validation error.</returns>
    Task<ServiceResult<string>> SaveAsync(
        int candidateId,
        string? fileName,
        long length,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a previously stored CV file. Unknown or foreign URLs are ignored.</summary>
    /// <param name="relativeUrl">Value previously returned by <see cref="SaveAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string? relativeUrl, CancellationToken cancellationToken = default);
}
