using System.Text;
using System.Text.RegularExpressions;
using hotjob.DTOs.Candidate;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed class RuleBasedResumeParser
{
    private static readonly Dictionary<string, string[]> Headings = new()
    {
        ["education"] = ["education", "hoc van", "trinh do hoc van"],
        ["work"] = ["experience", "work experience", "kinh nghiem", "kinh nghiem lam viec"],
        ["skills"] = ["skills", "technical skills", "ky nang", "ky nang chuyen mon"],
        ["summary"] = ["summary", "objective", "muc tieu nghe nghiep", "gioi thieu"],
        ["other"] = ["projects", "certifications", "languages", "references", "contact", "du an", "chung chi", "ngoai ngu", "nguoi tham chieu", "lien he"]
    };
    public ParsedResumeDto Parse(string text, IReadOnlyList<SkillOptionDto> catalog)
    {
        var sections = new Dictionary<string, StringBuilder>();
        var warnings = new List<string>();
        var education = new List<ParsedEducationDto>();
        var work = new List<ParsedWorkExperienceDto>();
        try
        {
            var section = "other";
            foreach (var original in text.Normalize().Replace("\r", "").Split('\n'))
            {
                var line = original.Trim();
                var headingLine = line.TrimStart('•', '*').Trim();
                var colon = headingLine.IndexOf(':');
                var heading = ResumeTextNormalization.Fold((colon >= 0 ? headingLine[..colon] : headingLine).Trim());
                var match = Headings.FirstOrDefault(h => h.Value.Contains(heading));
                if (match.Key is not null)
                {
                    section = match.Key;
                    line = colon >= 0 ? headingLine[(colon + 1)..].Trim() : "";
                }
                if (!sections.TryGetValue(section, out var target)) sections[section] = target = new();
                target.AppendLine(line);
            }
            string Section(string key) => sections.TryGetValue(key, out var value) ? value.ToString().Trim() : "";
            foreach (var block in Blocks(Section("education"), true).Take(21))
            {
                var school = Label(block, "school", "university", "truong");
                school ??= block.FirstOrDefault(l => Cue(l, @"\b(university|college|school|institute|dai hoc|cao dang|hoc vien)\b"));
                if (school is null) { warnings.Add("An education block could not be identified."); continue; }
                var degree = Label(block, "degree", "bang cap");
                var field = Label(block, "major", "field of study", "chuyen nganh", "nganh");
                degree ??= block.FirstOrDefault(l => Cue(l, @"\b(bachelor|master|doctor|phd|cu nhan|ky su|thac si|tien si)\b") && l != school);
                if (degree is not null && field is null && degree.Contains(" - "))
                { var parts = degree.Split(" - ", 2); degree = parts[0]; field = parts[1]; }
                var dates = new ResumeDateParser().ParseRange(string.Join("\n", block));
                warnings.AddRange(dates.Warnings);
                education.Add(new(new() { SchoolName = school, StartDate = dates.StartDate, EndDate = dates.EndDate,
                    Description = Description(block, school, degree, field) },
                    degree, field, Label(block, "school", "university", "truong") is null ? ResumeParseConfidence.Low : ResumeParseConfidence.Medium));
            }
            foreach (var block in Blocks(Section("work"), false).Take(21))
            {
                var company = Label(block, "company", "cong ty", "employer");
                company ??= block.FirstOrDefault(l => Cue(l, @"\b(company|corp|inc|ltd|cong ty|jsc|llc)\b"));
                if (company is null) { warnings.Add("A work experience block could not be identified."); continue; }
                var title = Label(block, "job title", "title", "position", "chuc danh", "vi tri");
                title ??= block.FirstOrDefault(l => l != company && Cue(l, @"\b(developer|engineer|manager|analyst|lap trinh|ky su|chuyen vien)\b"));
                var dates = new ResumeDateParser().ParseRange(string.Join("\n", block));
                warnings.AddRange(dates.Warnings);
                work.Add(new(new() { CompanyName = company, JobTitle = title, StartDate = dates.StartDate,
                    EndDate = dates.EndDate, Description = Description(block, company, title) }, dates.IsCurrent, ResumeParseConfidence.Medium));
            }
            var skillMatches = new ResumeSkillMatcher().Match(text, Section("skills"), catalog);
            var summary = Section("summary");
            return new ParsedResumeNormalizer().Normalize(new()
            {
                Summary = summary, SummaryConfidence = ResumeParseConfidence.High, Educations = education,
                WorkExperiences = work, Skills = skillMatches.Skills, UnrecognizedSkills = skillMatches.UnrecognizedSkills,
                Warnings = warnings.Concat(skillMatches.Warnings).ToArray()
            }, catalog);
        }
        catch (RegexMatchTimeoutException)
        {
            warnings.Add("Some sections could not be parsed within the time limit.");
            return new ParsedResumeNormalizer().Normalize(new() { Educations = education, WorkExperiences = work, Warnings = warnings }, catalog);
        }
    }
    private static bool Cue(string line, string pattern) => Regex.IsMatch(
        ResumeTextNormalization.Fold(line), pattern, RegexOptions.None, ResumeTextNormalization.RegexTimeout);
    private static string? Label(IEnumerable<string> lines, params string[] labels)
    {
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && labels.Contains(ResumeTextNormalization.Fold(line[..colon].Trim())))
                return ResumeTextNormalization.Clean(line[(colon + 1)..], 100_000);
        }
        return null;
    }
    private static IEnumerable<string[]> Blocks(string text, bool education)
    {
        var current = new List<string>();
        bool Organization(string line) => education
            ? Label([line], "school", "university", "truong") is not null
              || Cue(line, @"\b(university|college|school|institute|dai hoc|cao dang|hoc vien)\b")
            : Label([line], "company", "cong ty", "employer") is not null
              || Cue(line, @"\b(company|corp|inc|ltd|cong ty|jsc|llc)\b");
        bool Range(string line) => Cue(line, @"[0-9]{4}.*[-–—].*([0-9]{4}|present|hien tai)");
        foreach (var original in text.Split('\n').Select(l => l.Trim()))
        {
            var line = original.TrimStart('•', '*', '-').Trim();
            var begins = education ? Label([line], "school", "university", "truong") is not null
                : Label([line], "company", "cong ty", "employer") is not null;
            var organizationTransition = line.Length is > 0 and <= 200 && !line.EndsWith('.')
                && Organization(line) && current.Any(Organization)
                && !Cue(line, @"^(built|developed|managed|led|worked|xay dung|phat trien|quan ly)\b");
            var dateTransition = Range(line) && current.Any(Organization) && current.Any(Range);
            if (current.Count > 0 && (line.Length == 0 || begins || organizationTransition || dateTransition))
            { yield return current.ToArray(); current.Clear(); }
            if (line.Length > 0) current.Add(line);
        }
        if (current.Count > 0) yield return current.ToArray();
    }
    private static string? Description(string[] block, params string?[] known) => ResumeTextNormalization.Clean(
        string.Join("\n", block.Where(l => !known.Contains(l) && !l.Contains(':')
            && !Cue(l, @"\b\d{4}\b"))), 100_000);
}
