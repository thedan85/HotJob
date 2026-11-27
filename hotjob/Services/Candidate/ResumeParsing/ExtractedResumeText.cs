namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed record ExtractedResumeText(string Text, IReadOnlyList<string> Warnings);
