using System.Text.RegularExpressions;
using hotjob.DTOs.Candidate;

namespace hotjob.Services.Candidate.ResumeParsing;

internal sealed record ResumeSkillResolution(SkillOptionDto? Skill, bool IsAlias, bool HasConflict);
internal sealed record ResumeSkillMatches(IReadOnlyList<ParsedSkillDto> Skills,
    IReadOnlyList<string> UnrecognizedSkills, IReadOnlyList<string> Warnings);

internal sealed class ResumeSkillMatcher
{
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        ["c#"] = ["c sharp", "csharp"], ["c++"] = ["c plus plus", "cpp"],
        [".net"] = ["dotnet", "dot net"], ["node.js"] = ["nodejs", "node js"],
        ["asp.net core"] = ["asp.netcore", "asp net core"], ["spring boot"] = ["springboot"]
    };
    private static IEnumerable<string> Names(SkillOptionDto skill)
    {
        var name = ResumeTextNormalization.Fold(skill.Name);
        yield return name;
        if (Aliases.TryGetValue(name, out var aliases))
            foreach (var alias in aliases) yield return alias;
    }
    public ResumeSkillResolution Resolve(string? name, int? id, IReadOnlyList<SkillOptionDto> catalog)
    {
        var folded = ResumeTextNormalization.Fold(name?.Trim() ?? "");
        var named = folded.Length == 0 ? null : catalog.FirstOrDefault(s => Names(s).Contains(folded));
        var numbered = id is > 0 ? catalog.FirstOrDefault(s => s.Id == id) : null;
        var conflict = id.HasValue && (id <= 0 || folded.Length > 0 && (named is null || numbered?.Id != named.Id));
        return new(conflict ? null : named ?? (folded.Length == 0 ? numbered : null),
            named is not null && ResumeTextNormalization.Fold(named.Name) != folded, conflict);
    }
    public ResumeSkillMatches Match(string text, string? skillsSection, IReadOnlyList<SkillOptionDto> catalog)
    {
        var skills = new Dictionary<int, ParsedSkillDto>();
        var warnings = new List<string>();
        string RemoveMatches(string input, bool collect)
        {
            var folded = ResumeTextNormalization.Fold(input);
            var occupied = new bool[folded.Length];
            foreach (var item in catalog.SelectMany(s => Names(s).Select(n => (Skill: s, Name: n)))
                         .OrderByDescending(x => x.Name.Length))
            {
                var start = 0;
                while ((start = folded.IndexOf(item.Name, start, StringComparison.Ordinal)) >= 0)
                {
                    var end = start + item.Name.Length;
                    static bool Token(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or '+' or '#';
                    if ((start == 0 || !Token(folded[start - 1])) && (end == folded.Length || !Token(folded[end]))
                        && !occupied.AsSpan(start, end - start).Contains(true))
                    {
                        occupied.AsSpan(start, end - start).Fill(true);
                        if (collect) skills.TryAdd(item.Skill.Id, new(new() { SkillId = item.Skill.Id }, item.Skill.Name,
                            item.Name == ResumeTextNormalization.Fold(item.Skill.Name) ? ResumeParseConfidence.High : ResumeParseConfidence.Medium));
                    }
                    start = end;
                }
            }
            var chars = folded.ToCharArray();
            for (var i = 0; i < chars.Length; i++) if (occupied[i]) chars[i] = ' ';
            // Unknown strings should retain source spelling, including Vietnamese diacritics.
            // Match each original list entry separately below rather than reusing folded indices.
            return new string(chars);
        }
        RemoveMatches(text, true);
        var unknown = new List<string>();
        if (skillsSection is not null)
            foreach (var part in skillsSection.Split([',', ';', '\n', '•', '|'], StringSplitOptions.RemoveEmptyEntries).Take(1000))
            {
                var remainder = RemoveMatches(part, false).Trim(' ', '-', ':', '/', '*');
                if (remainder.Length == 0) continue;
                remainder = Regex.Replace(remainder, @"\([^)]*\)|\b(beginner|intermediate|advanced|expert)\b", "",
                    RegexOptions.IgnoreCase, ResumeTextNormalization.RegexTimeout).Trim();
                if (remainder.Length > 0) unknown.Add(ResumeTextNormalization.Clean(
                    string.Equals(ResumeTextNormalization.Fold(part.Trim()), remainder, StringComparison.Ordinal) ? part.Trim() : remainder, 100)!);
            }
        return new(skills.Values.ToArray(), unknown.Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray(), warnings);
    }
}
