using hotjob.DTOs.Candidate;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class ParsedResumeNormalizer
{
    public ParsedResumeDto Normalize(ParsedResumeDto draft, IReadOnlyList<SkillOptionDto> catalog)
    {
        var warnings = new List<string>(draft.Warnings);
        string? Clean(string? value, int max, string field)
        {
            if (value?.Length > max) warnings.Add($"{field} was shortened to its supported length.");
            return ResumeTextNormalization.Clean(value, max);
        }
        static ResumeParseConfidence Confidence(ResumeParseConfidence value) =>
            Enum.IsDefined(value) ? value : ResumeParseConfidence.Low;
        (DateOnly? Start, DateOnly? End) Dates(DateOnly? start, DateOnly? end)
        {
            if (start.HasValue && end < start)
            { warnings.Add("A reversed date range was omitted."); return (null, null); }
            return (start, end);
        }
        var education = new List<ParsedEducationDto>();
        foreach (var row in draft.Educations.Take(20))
        {
            var school = Clean(row.Value.SchoolName, 200, "School name");
            if (school is null) { warnings.Add("An education without a school was omitted."); continue; }
            var degree = Clean(row.Degree, 200, "Degree");
            var field = Clean(row.FieldOfStudy ?? (row.Degree is null ? row.Value.Major : null), 200, "Field of study");
            var major = degree is null ? field : field is null || ResumeTextNormalization.Fold(degree) == ResumeTextNormalization.Fold(field)
                ? degree : $"{degree} - {field}";
            var dates = Dates(row.Value.StartDate, row.Value.EndDate);
            var gpa = row.Value.Gpa;
            if (gpa.HasValue && (!double.IsFinite(gpa.Value) || gpa is < 0 or > 4))
            { gpa = null; warnings.Add("An invalid GPA was omitted."); }
            education.Add(new(new() { SchoolName = school, Major = Clean(major, 200, "Major"), StartDate = dates.Start,
                EndDate = dates.End, Gpa = gpa, Description = Clean(row.Value.Description, 2000, "Education description") },
                degree, field, Confidence(row.Confidence)));
        }
        var work = new List<ParsedWorkExperienceDto>();
        foreach (var row in draft.WorkExperiences.Take(20))
        {
            var company = Clean(row.Value.CompanyName, 200, "Company name");
            if (company is null) { warnings.Add("An experience without a company was omitted."); continue; }
            var dates = Dates(row.Value.StartDate, row.IsCurrent ? null : row.Value.EndDate);
            work.Add(new(new() { CompanyName = company, JobTitle = Clean(row.Value.JobTitle, 200, "Job title"),
                StartDate = dates.Start, EndDate = dates.End, Description = Clean(row.Value.Description, 2000, "Work description") },
                row.IsCurrent, Confidence(row.Confidence)));
        }
        var skills = new Dictionary<int, ParsedSkillDto>();
        var unknown = new List<string>(draft.UnrecognizedSkills);
        var matcher = new ResumeSkillMatcher();
        foreach (var row in draft.Skills.Take(1000))
        {
            var resolved = matcher.Resolve(row.SkillName, row.Value.SkillId != 0 ? row.Value.SkillId : null, catalog);
            if (resolved.Skill is null)
            {
                warnings.Add("An unknown or conflicting skill was not selected.");
                if (!string.IsNullOrWhiteSpace(row.SkillName) && matcher.Resolve(row.SkillName, null, catalog).Skill is null)
                    unknown.Add(row.SkillName);
                continue;
            }
            if (skills.Count < 100) skills.TryAdd(resolved.Skill.Id,
                new(new() { SkillId = resolved.Skill.Id }, resolved.Skill.Name, Confidence(row.Confidence)));
        }
        if (draft.Educations.Count > 20 || draft.WorkExperiences.Count > 20 || draft.Skills.Count > 100 || unknown.Count > 100)
            warnings.Add("Some suggestions were omitted because item limits were reached.");
        if (education.Count == 0) warnings.Add("No education suggestions were found.");
        if (work.Count == 0) warnings.Add("No work experience suggestions were found.");
        if (skills.Count == 0) warnings.Add("No catalog skills were found. Add at least one skill before saving.");
        var summary = Clean(draft.Summary, 2000, "Summary");
        if (summary is null) warnings.Add("No summary section was found.");
        var boundedUnknown = unknown.Select(s => Clean(s, 100, "Unknown skill")).Where(s => s is not null)
            .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
        var boundedWarnings = warnings.Select(w => ResumeTextNormalization.Clean(w, 300)!)
            .Where(w => !string.IsNullOrWhiteSpace(w)).Distinct().Take(51).ToList();
        if (boundedWarnings.Count > 50) { boundedWarnings = boundedWarnings.Take(49).ToList(); boundedWarnings.Add("Additional warnings were omitted."); }
        return draft with { Summary = summary, SummaryConfidence = summary is null ? null : Confidence(draft.SummaryConfidence ?? ResumeParseConfidence.Low),
            Educations = education, WorkExperiences = work, Skills = skills.Values.ToArray(),
            UnrecognizedSkills = boundedUnknown, Warnings = boundedWarnings };
    }
}
