using hotjob.DTOs.Candidate;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

public interface IResumeParserService
{
    /// <summary>Configured capability, not a reservation or uptime guarantee.</summary>
    bool IsAiAvailable { get; }
    Task<ServiceResult<ParsedResumeDto>> ParseAsync(string? userId, Stream file, string fileName,
        ResumeParseMode mode = ResumeParseMode.Rules, CancellationToken ct = default);
}
