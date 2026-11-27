namespace hotjob.DTOs.Candidate;

public enum ResumeParseMode { Rules, Ai }
public enum ResumeParseConfidence { High, Medium, Low }

/// <summary>Unsaved suggestions only. A candidate must review these before any resume write.</summary>
public sealed record ParsedResumeDto
{
    public string? Summary { get; init; }
    public ResumeParseConfidence? SummaryConfidence { get; init; }
    public IReadOnlyList<ParsedEducationDto> Educations { get; init; } = [];
    public IReadOnlyList<ParsedWorkExperienceDto> WorkExperiences { get; init; } = [];
    public IReadOnlyList<ParsedSkillDto> Skills { get; init; } = [];
    public ResumeParseMode ModeUsed { get; init; } = ResumeParseMode.Rules;
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> UnrecognizedSkills { get; init; } = [];
}

public sealed record ParsedEducationDto(ResumeEducationInput Value, string? Degree, string? FieldOfStudy, ResumeParseConfidence Confidence);
public sealed record ParsedWorkExperienceDto(ResumeWorkExperienceInput Value, bool IsCurrent, ResumeParseConfidence Confidence);
public sealed record ParsedSkillDto(ResumeSkillInput Value, string SkillName, ResumeParseConfidence Confidence);
