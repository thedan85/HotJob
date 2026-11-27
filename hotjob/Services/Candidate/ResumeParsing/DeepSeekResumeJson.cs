using System.Globalization;
using System.Text.Json;
using hotjob.DTOs.Candidate;
using hotjob.Services.Common;

namespace hotjob.Services.Candidate.ResumeParsing;

internal static class DeepSeekResumeJson
{
    public static ServiceResult<ParsedResumeDto> Parse(ReadOnlyMemory<byte> response)
    {
        try
        {
            using var outer = JsonDocument.Parse(response, new() { MaxDepth = 16 });
            UniqueProperties(outer.RootElement);
            var choice = outer.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop") return Fail();
            var message = choice.GetProperty("message");
            if (message.TryGetProperty("tool_calls", out var tools) && tools.ValueKind != JsonValueKind.Null && tools.GetArrayLength() > 0) return Fail();
            if (message.GetProperty("role").GetString() != "assistant") return Fail();
            using var inner = JsonDocument.Parse(message.GetProperty("content").GetString()!, new() { MaxDepth = 16 });
            var root = inner.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail();
            UniqueProperties(root);
            if (!new[] { "summary", "educations", "workExperiences", "skills" }.Any(n => root.TryGetProperty(n, out _))) return Fail();
            var warnings = new List<string>();
            string? String(JsonElement item, string name)
            {
                if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
                return value.GetString();
            }
            DateOnly? Date(JsonElement item, string name)
            {
                var value = String(item, name);
                if (value is null) return null;
                if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
                warnings.Add("An invalid AI date was omitted.");
                return null;
            }
            IEnumerable<JsonElement> Items(string name, int limit)
            {
                if (!root.TryGetProperty(name, out var array) || array.ValueKind == JsonValueKind.Null) return [];
                return array.EnumerateArray().Take(limit).ToArray();
            }
            var educations = Items("educations", 21).Select(item => new ParsedEducationDto(new()
            {
                SchoolName = String(item, "schoolName") ?? "", StartDate = Date(item, "startDate"), EndDate = Date(item, "endDate"),
                Gpa = item.TryGetProperty("gpa", out var gpa) && gpa.ValueKind != JsonValueKind.Null ? gpa.GetDouble() : null,
                Description = String(item, "description")
            }, String(item, "degree"), String(item, "fieldOfStudy"), ResumeParseConfidence.Medium)).ToArray();
            var work = Items("workExperiences", 21).Select(item => new ParsedWorkExperienceDto(new()
            {
                CompanyName = String(item, "companyName") ?? "", JobTitle = String(item, "jobTitle"),
                StartDate = Date(item, "startDate"), EndDate = Date(item, "endDate"), Description = String(item, "description")
            }, item.TryGetProperty("isCurrent", out var current) && current.ValueKind != JsonValueKind.Null && current.GetBoolean(),
                ResumeParseConfidence.Medium)).ToArray();
            var skills = Items("skills", 1001).Select(item => new ParsedSkillDto(new()
            {
                // Zero means absent in the existing DTO; reserve a negative draft sentinel
                // for an explicitly supplied zero so catalog validation cannot erase presence.
                SkillId = item.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null
                    ? id.GetInt32() is var provided && provided == 0 ? int.MinValue : provided : 0
            }, String(item, "name") ?? "", ResumeParseConfidence.Medium)).ToArray();
            _ = String(root, "summary"); // Validate type, but the final summary is always owned by source extraction.
            return ServiceResult<ParsedResumeDto>.Ok(new()
            { Educations = educations, WorkExperiences = work, Skills = skills, Warnings = warnings, ModeUsed = ResumeParseMode.Ai });
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException
                                     or IndexOutOfRangeException or FormatException or OverflowException)
        { return Fail(); }
    }
    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                UniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) UniqueProperties(item);
    }
    private static ServiceResult<ParsedResumeDto> Fail() => ServiceResult<ParsedResumeDto>.Fail(
        ServiceErrorType.Validation, "AI returned an unreadable suggestion response.");
}
