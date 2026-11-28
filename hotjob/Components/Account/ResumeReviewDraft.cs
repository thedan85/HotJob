using hotjob.DTOs.Candidate;
using hotjob.Services.Candidate.ResumeParsing;

namespace hotjob.Components.Account;

/// <summary>
/// Mutable, unsaved copy of a parsed resume. No service calls and no writes:
/// mapping and validation only. Every nested value is copied so the source DTO
/// can never be altered by candidate edits.
/// </summary>
public sealed class ResumeReviewDraft
{
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public ResumeParseConfidence? SummaryConfidence { get; set; }
    public ResumeParseMode ModeUsed { get; set; } = ResumeParseMode.Rules;
    public List<ReviewEducation> Educations { get; } = [];
    public List<ReviewWork> WorkExperiences { get; } = [];
    public List<ReviewSkill> Skills { get; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public IReadOnlyList<string> UnrecognizedSkills { get; set; } = [];

    /// <summary>Stable row identity for Blazor @key so edits/removals never reuse a wrong row.</summary>
    public sealed class ReviewEducation
    {
        public Guid Key { get; init; } = Guid.NewGuid();
        public string SchoolName { get; set; } = string.Empty;
        public string? Degree { get; set; }
        public string? FieldOfStudy { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public double? Gpa { get; set; }
        public string? Description { get; set; }
        public ResumeParseConfidence Confidence { get; set; } = ResumeParseConfidence.Low;
        public bool IsManual { get; init; }

        /// <summary>Preview of the mapped Major, recomputed from the edited metadata.</summary>
        public string? MajorPreview => ComputeMajor().Major;
        public bool MajorShortened => ComputeMajor().Shortened;

        /// <summary>Backend-consistent whitespace/NFC cleanup, dedup and 200-char cap.</summary>
        public (string? Major, bool Shortened) ComputeMajor()
        {
            var shortened = (Degree?.Length ?? 0) > 200 || (FieldOfStudy?.Length ?? 0) > 200;
            var degree = ResumeTextNormalization.Clean(Degree, 200);
            var field = ResumeTextNormalization.Clean(FieldOfStudy, 200);
            var major = degree is null
                ? field
                : field is null || ResumeTextNormalization.Fold(degree) == ResumeTextNormalization.Fold(field)
                    ? degree
                    : $"{degree} - {field}";
            if (major is null || major.Length <= 200) return (major, shortened);
            return (ResumeTextNormalization.Limit(major, 200), true);
        }
    }

    public sealed class ReviewWork
    {
        public Guid Key { get; init; } = Guid.NewGuid();
        public string CompanyName { get; set; } = string.Empty;
        public string? JobTitle { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? Description { get; set; }
        public bool IsCurrent { get; set; }
        public ResumeParseConfidence Confidence { get; set; } = ResumeParseConfidence.Low;
        public bool IsManual { get; init; }
    }

    public sealed class ReviewSkill
    {
        public Guid Key { get; init; } = Guid.NewGuid();
        public int SkillId { get; set; }
        public string SkillName { get; set; } = string.Empty;
        public int YearsOfExperience { get; set; }
        public ResumeParseConfidence Confidence { get; set; } = ResumeParseConfidence.Low;
        public bool IsManual { get; init; }
    }

    public static ResumeReviewDraft FromParsed(ParsedResumeDto parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        var draft = new ResumeReviewDraft
        {
            Summary = parsed.Summary,
            SummaryConfidence = parsed.SummaryConfidence,
            ModeUsed = parsed.ModeUsed,
            Warnings = parsed.Warnings.ToArray(),
            UnrecognizedSkills = parsed.UnrecognizedSkills.ToArray()
        };
        foreach (var item in parsed.Educations)
        {
            var value = item.Value;
            // Original Major is only a field-of-study fallback when no degree/field metadata exists.
            var field = item.FieldOfStudy ?? (item.Degree is null ? value.Major : null);
            draft.Educations.Add(new ReviewEducation
            {
                SchoolName = value.SchoolName ?? string.Empty,
                Degree = item.Degree,
                FieldOfStudy = field,
                StartDate = value.StartDate,
                EndDate = value.EndDate,
                Gpa = value.Gpa,
                Description = value.Description,
                Confidence = item.Confidence
            });
        }
        foreach (var item in parsed.WorkExperiences)
        {
            var value = item.Value;
            draft.WorkExperiences.Add(new ReviewWork
            {
                CompanyName = value.CompanyName ?? string.Empty,
                JobTitle = value.JobTitle,
                StartDate = value.StartDate,
                EndDate = value.EndDate,
                Description = value.Description,
                IsCurrent = item.IsCurrent,
                Confidence = item.Confidence
            });
        }
        foreach (var item in parsed.Skills)
        {
            draft.Skills.Add(new ReviewSkill
            {
                SkillId = item.Value.SkillId,
                SkillName = item.SkillName,
                YearsOfExperience = item.Value.YearsOfExperience,
                Confidence = item.Confidence
            });
        }
        return draft;
    }

    /// <summary>Fresh input snapshot for Save; recomputes Major and current-work EndDate.</summary>
    public ResumeInput ToInput() => new()
    {
        Title = Title,
        Summary = Summary,
        Educations = Educations.Select(item => new ResumeEducationInput
        {
            SchoolName = item.SchoolName,
            Major = item.ComputeMajor().Major,
            StartDate = item.StartDate,
            EndDate = item.EndDate,
            Gpa = item.Gpa,
            Description = item.Description
        }).ToList(),
        WorkExperiences = WorkExperiences.Select(item => new ResumeWorkExperienceInput
        {
            CompanyName = item.CompanyName,
            JobTitle = item.JobTitle,
            StartDate = item.StartDate,
            EndDate = item.IsCurrent ? null : item.EndDate,
            Description = item.Description
        }).ToList(),
        Skills = Skills.Select(item => new ResumeSkillInput
        {
            SkillId = item.SkillId,
            YearsOfExperience = item.YearsOfExperience
        }).ToList()
    };

    /// <summary>All client-side errors; an empty list means the draft is safe to save.</summary>
    public IReadOnlyList<string> Validate(IReadOnlyList<SkillOptionDto> catalog)
    {
        var errors = new List<string>();
        var title = Title?.Trim() ?? string.Empty;
        if (title.Length == 0) errors.Add("Resume title is required.");
        else if (title.Length > 200) errors.Add("Resume title must be at most 200 characters.");
        if (Summary?.Trim().Length > 2000) errors.Add("Resume summary must be at most 2000 characters.");

        if (Educations.Count > 20) errors.Add("A resume can have at most 20 education entries.");
        for (var index = 0; index < Educations.Count; index++)
        {
            var item = Educations[index];
            var label = $"Education entry {index + 1}";
            var school = item.SchoolName?.Trim() ?? string.Empty;
            if (school.Length == 0) errors.Add($"{label}: school name is required.");
            else if (school.Length > 200) errors.Add($"{label}: school name must be at most 200 characters.");
            if (item.Degree?.Trim().Length > 200) errors.Add($"{label}: degree must be at most 200 characters.");
            if (item.FieldOfStudy?.Trim().Length > 200) errors.Add($"{label}: field of study must be at most 200 characters.");
            if (item.Description?.Trim().Length > 2000) errors.Add($"{label}: description must be at most 2000 characters.");
            if (item.Gpa.HasValue && (!double.IsFinite(item.Gpa.Value) || item.Gpa is < 0 or > 4))
                errors.Add($"{label}: GPA must be between 0 and 4.");
            if (item.StartDate.HasValue && item.EndDate.HasValue && item.EndDate < item.StartDate)
                errors.Add($"{label}: end date cannot be before the start date.");
        }

        if (WorkExperiences.Count > 20) errors.Add("A resume can have at most 20 work experience entries.");
        for (var index = 0; index < WorkExperiences.Count; index++)
        {
            var item = WorkExperiences[index];
            var label = $"Work experience entry {index + 1}";
            var company = item.CompanyName?.Trim() ?? string.Empty;
            // A stale end date on a current job is ignored by ToInput as well.
            if (!item.IsCurrent && item.StartDate.HasValue && item.EndDate.HasValue && item.EndDate < item.StartDate)
                errors.Add($"{label}: end date cannot be before the start date.");
            if (company.Length == 0) errors.Add($"{label}: company name is required.");
            else if (company.Length > 200) errors.Add($"{label}: company name must be at most 200 characters.");
            if (item.JobTitle?.Trim().Length > 200) errors.Add($"{label}: job title must be at most 200 characters.");
            if (item.Description?.Trim().Length > 2000) errors.Add($"{label}: description must be at most 2000 characters.");
        }

        if (Skills.Count == 0) errors.Add("Add at least one skill before saving.");
        if (Skills.GroupBy(skill => skill.SkillId).Any(group => group.Count() > 1))
            errors.Add("Each skill can only be added once per resume.");
        var validIds = catalog.Select(option => option.Id).ToHashSet();
        foreach (var skill in Skills)
        {
            if (skill.SkillId <= 0) errors.Add("Choose a valid skill.");
            else if (!validIds.Contains(skill.SkillId)) errors.Add("One or more selected skills no longer exist.");
            if (skill.YearsOfExperience is < 0 or > 50)
                errors.Add("Skill experience must be between 0 and 50 years.");
        }
        return errors;
    }
}
