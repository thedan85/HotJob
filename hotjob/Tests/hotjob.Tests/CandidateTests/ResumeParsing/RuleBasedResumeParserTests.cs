using System.Text;
using hotjob.DTOs.Candidate;
using hotjob.Services.Candidate.ResumeParsing;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class RuleBasedResumeParserTests
{
    internal const string English = """
        Summary
        I build reliable software.
        Education
        School: Example University
        Degree: Bachelor of Science
        Major: Computer Science
        2021-2024
        Work Experience
        Company: Example Company
        Job title: Developer
        Sep 2021 - Present
        Built reliable APIs.
        Technical Skills
        C#, .NET, Rust
        """;
    [Theory]
    [InlineData("Education", "Experience", "Skills", "Summary", false)]
    [InlineData("Học vấn", "Kinh nghiệm", "Kỹ năng", "Giới thiệu", false)]
    [InlineData("Trình độ học vấn", "Kinh nghiệm làm việc", "Kỹ năng chuyên môn", "Mục tiêu nghề nghiệp", true)]
    public void AllSections_BilingualAndUnicode(string education, string work, string skills, string summary, bool nfd)
    {
        var text = English.Replace("Education", education).Replace("Work Experience", work)
            .Replace("Technical Skills", skills).Replace("Summary", summary);
        if (nfd) text = text.Normalize(NormalizationForm.FormD);
        var result = new RuleBasedResumeParser().Parse(text, ParsedResumeNormalizerTests.Catalog);
        Assert.Equal("Bachelor of Science - Computer Science", Assert.Single(result.Educations).Value.Major);
        Assert.Equal("Example Company", Assert.Single(result.WorkExperiences).Value.CompanyName);
        Assert.True(result.WorkExperiences[0].IsCurrent);
        Assert.Equal(2, result.Skills.Count);
        Assert.Contains("Rust", result.UnrecognizedSkills);
        Assert.Equal("I build reliable software.", result.Summary);
    }
    [Theory]
    [InlineData("2021-2024", "2021-01-01", "2024-12-31", false)]
    [InlineData("09/2021 – 06/2023", "2021-09-01", "2023-06-30", false)]
    [InlineData("Sep 2021 - Present", "2021-09-01", null, true)]
    [InlineData("Tháng 9/2021 - Hiện tại", "2021-09-01", null, true)]
    [InlineData("2021-09-15 - 2023-06-20", "2021-09-15", "2023-06-20", false)]
    public void Dates(string source, string start, string? end, bool current)
    {
        var result = new ResumeDateParser().ParseRange(source);
        Assert.Equal(DateOnly.Parse(start), result.StartDate);
        Assert.Equal(end is null ? (DateOnly?)null : DateOnly.Parse(end), result.EndDate);
        Assert.Equal(current, result.IsCurrent);
    }
    [Fact]
    public void MissingSectionsAndReversedDates_DoNotInvent()
    {
        var result = new RuleBasedResumeParser().Parse("An unrelated paragraph.", []);
        Assert.Empty(result.Educations);
        Assert.Empty(result.WorkExperiences);
        Assert.Null(result.Summary);
        Assert.NotEmpty(result.Warnings);
        var dates = new ResumeDateParser().ParseRange("2024-2021");
        Assert.Null(dates.StartDate);
        Assert.Null(dates.EndDate);
    }
}
