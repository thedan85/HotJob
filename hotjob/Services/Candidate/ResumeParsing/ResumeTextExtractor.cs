using hotjob.Services.Common;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class ResumeTextExtractor(
    ResumeExtractionRunner runner, PdfResumeTextExtractor pdf, DocxResumeTextExtractor docx)
{
    public Task<ServiceResult<ExtractedResumeText>> ExtractAsync(ValidatedCvFile file, CancellationToken ct) =>
        file.Extension switch
        {
            ".pdf" => runner.RunAsync(token => pdf.Extract(file.Bytes, token), ct),
            ".docx" => runner.RunAsync(token => docx.Extract(file.Bytes, token), ct),
            _ => Task.FromResult(ServiceResult<ExtractedResumeText>.Fail(ServiceErrorType.Validation,
                "This file type cannot be read automatically. Please fill in manually."))
        };
}
