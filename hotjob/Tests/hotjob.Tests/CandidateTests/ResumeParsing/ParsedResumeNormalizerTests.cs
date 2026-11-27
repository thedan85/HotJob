using hotjob.DTOs.Candidate;
using hotjob.Services.Candidate.ResumeParsing;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ParsedResumeNormalizerTests
{
    internal static readonly SkillOptionDto[] Catalog =
    [
        new(1, "C#", null), new(2, ".NET", null), new(3, "C++", null), new(4, "Node.js", null),
        new(5, "ASP.NET Core", null), new(6, "Spring Boot", null), new(7, "Java", null),
        new(8, "JavaScript", null), new(9, "SQL", null), new(10, "MySQL", null), new(11, "Git", null)
    ];
    [Fact]
    public void DegreeMajor_MetadataAndLengthLimits()
    {
        var draft = new ParsedResumeDto
        {
            Summary = new string('a', 2001),
            Educations = Enumerable.Range(0, 21).Select(_ => new ParsedEducationDto(
                new() { SchoolName = "Example University", Gpa = double.NaN },
                "Bachelor of Science", "Computer Science", ResumeParseConfidence.Medium)).ToArray()
        };
        var result = new ParsedResumeNormalizer().Normalize(draft, Catalog);
        Assert.Equal(20, result.Educations.Count);
        var entry = result.Educations[0];
        Assert.Equal("Bachelor of Science - Computer Science", entry.Value.Major);
        Assert.Equal("Bachelor of Science", entry.Degree);
        Assert.Null(entry.Value.Gpa);
        Assert.Equal(2000, result.Summary!.Length);
        Assert.Equal(21, draft.Educations.Count);
        Assert.NotEmpty(result.Warnings);
    }
    [Theory]
    [InlineData("C#/.NET/C++, NodeJS; ASP.NET Core; SpringBoot", "1,2,3,4,5,6")]
    [InlineData("JavaScript MySQL GitHub C++Builder ASP.NET", "8,10")]
    [InlineData("C sharp, CPP, dotnet, Node JS, ASP NET Core", "1,2,3,4,5")]
    public void Skills_BoundariesAndAliases(string text, string expected)
    {
        var result = new ResumeSkillMatcher().Match(text, text, Catalog);
        Assert.Equal(expected, string.Join(",", result.Skills.Select(x => x.Value.SkillId).Order()));
    }
    [Fact]
    public void UnknownSkills_AndConflictingIdsAreNotRecognized()
    {
        var result = new ResumeSkillMatcher().Match("Rust, C++", "Rust, C++", Catalog.Where(x => x.Id != 3).ToArray());
        Assert.Empty(result.Skills);
        Assert.Contains("Rust", result.UnrecognizedSkills);
        var normalized = new ParsedResumeNormalizer().Normalize(new()
        {
            Skills = [new(new() { SkillId = 2 }, "C#", ResumeParseConfidence.Medium),
                new(new() { SkillId = 999 }, "UnknownTool", ResumeParseConfidence.Medium)]
        }, Catalog);
        Assert.Empty(normalized.Skills);
        Assert.Contains("UnknownTool", normalized.UnrecognizedSkills);
    }
}
