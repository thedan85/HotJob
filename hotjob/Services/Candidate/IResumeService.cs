using hotjob.DTOs.Candidate;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

/// <summary>CRUD and file upload for the signed-in candidate's own resumes.</summary>
public interface IResumeService
{
    /// <summary>Lists the caller's resumes, primary first.</summary>
    Task<ServiceResult<IReadOnlyList<ResumeListItemDto>>> ListAsync(
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one of the caller's resumes with education, experience and skills.</summary>
    Task<ServiceResult<ResumeDetailDto>> GetAsync(
        string? userId,
        int resumeId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a resume for the caller. The first resume becomes primary.</summary>
    Task<ServiceResult<ResumeDetailDto>> CreateAsync(
        string? userId,
        ResumeInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the caller's resume content (children are replaced atomically).</summary>
    Task<ServiceResult<ResumeDetailDto>> UpdateAsync(
        string? userId,
        int resumeId,
        ResumeInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one of the caller's resumes that was never used to apply.</summary>
    Task<ServiceResult> DeleteAsync(
        string? userId,
        int resumeId,
        CancellationToken cancellationToken = default);

    /// <summary>Makes one resume primary and clears the flag on all others.</summary>
    Task<ServiceResult<ResumeDetailDto>> SetPrimaryAsync(
        string? userId,
        int resumeId,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a PDF/DOC/DOCX CV file (max 5 MB) and points the resume at it.</summary>
    Task<ServiceResult<ResumeDetailDto>> UploadFileAsync(
        string? userId,
        int resumeId,
        string? fileName,
        long length,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every skill the candidate can attach to a resume.</summary>
    Task<ServiceResult<IReadOnlyList<SkillOptionDto>>> ListSkillsAsync(
        CancellationToken cancellationToken = default);
}
