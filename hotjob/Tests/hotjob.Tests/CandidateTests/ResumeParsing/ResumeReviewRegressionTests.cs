using System.Text;
using hotjob.DTOs.Candidate;
using hotjob.Services.Candidate;
using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support.ResumeParsing;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ResumeReviewRegressionTests
{
    [Theory]
    [InlineData("2021-2024 0912 345 678", "2021-2024", "0912")]
    [InlineData("2021-2024 (3 years)", "2021-2024 (3 years)", null)]
    [InlineData("0912345678 0987654321", null, "0912345678")]
    [InlineData("09/2021 – 06/2023 +84 912 345 678", "09/2021 – 06/2023", "912")]
    public void Redaction_SeparatesDatesAndPhones(string source, string? preserved, string? removed)
    {
        var result = new ResumeContactRedactor(TimeProvider.System).Redact("Skills: C#\n" + source);
        Assert.True(result.Success);
        if (preserved is not null) Assert.Contains(preserved, result.Data!);
        if (removed is not null) Assert.DoesNotContain(removed, result.Data!);
    }

    [Fact]
    public void Redaction_AdjacentDatePhoneNearCutoff()
    {
        var result = new ResumeContactRedactor(TimeProvider.System).Redact(
            "Skills: C#\n" + new string('a', 29_960) + "\n2021-2024 0912 345 678");
        Assert.True(result.Success);
        Assert.DoesNotContain("0912", result.Data!);
        Assert.Contains("2021-2024", result.Data!);
    }

    [Fact]
    public async Task SurrogateBoundary_CompleteServiceDoesNotThrow()
    {
        await using var c = await ResumeParserTestContext.CreateAsync();
        const string prefix = "Skills: C#\n";
        var text = prefix + new string('a', 99_999 - prefix.Length) + "\U0001F600";
        using var stream = new MemoryStream(ResumeDocumentFactory.Docx(text, false));
        var result = await c.Service.ParseAsync(c.UserId, stream, "cv.docx");
        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Single(result.Data!.Skills);
        var extracted = new DocxResumeTextExtractor().Extract(ResumeDocumentFactory.Docx(text, false), default);
        Assert.True(extracted.Text.IsNormalized());
        Assert.Equal(99_999, extracted.Text.Length);
    }

    [Theory]
    [InlineData("２０２１-２０２４")]
    [InlineData("٢٠٢١-٢٠٢٤")]
    public void UnsupportedDecimalDates_AreOmittedWithoutThrowing(string source)
    {
        var result = new ResumeDateParser().ParseRange(source);
        Assert.Null(result.StartDate);
        Assert.Null(result.EndDate);
        Assert.NotEmpty(result.Warnings);
        var parsed = new RuleBasedResumeParser().Parse("Education\nSchool: Example University\n" + source, []);
        Assert.Single(parsed.Educations);
    }

    [Fact]
    public void DateRangeTakesPrecedenceOverIncidentalYear()
    {
        var result = new RuleBasedResumeParser().Parse(
            "Education\nSchool: University 2000\n2021-2024", []);
        var entry = Assert.Single(result.Educations).Value;
        Assert.Equal(new DateOnly(2021, 1, 1), entry.StartDate);
        Assert.Equal(new DateOnly(2024, 12, 31), entry.EndDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnlabeledOrganizations_AreSeparateBlocks(bool vietnamese)
    {
        var text = vietnamese ? """
            Kinh nghiệm làm việc
            • Công ty Ví Dụ
            Lập trình viên
            2021-2022
            • Công ty Khác
            Kỹ sư
            2023-2024
            Học vấn
            • Đại học Ví Dụ
            Cử nhân
            2017-2020
            • Đại học Khác
            Thạc sĩ
            2021-2022
            """ : """
            Work Experience
            • Example Company
            Developer
            2021-2022
            • Other Company
            Engineer
            2023-2024
            Education
            • Example University
            Bachelor
            2017-2020
            • Other University
            Master
            2021-2022
            """;
        var result = new RuleBasedResumeParser().Parse(text, []);
        Assert.Equal(2, result.WorkExperiences.Count);
        Assert.Equal(2, result.Educations.Count);
        Assert.Equal(new DateOnly(2023, 1, 1), result.WorkExperiences[1].Value.StartDate);
        Assert.DoesNotContain(vietnamese ? "Công ty Khác" : "Other Company", result.WorkExperiences[0].Value.Description ?? "");
    }

    [Fact]
    public async Task SourceSummaryTruncationWarningSurvivesAiSuccess()
    {
        await using var c = await ResumeParserTestContext.CreateAsync(true);
        using var stream = new MemoryStream(ResumeDocumentFactory.Docx("Summary\n" + new string('a', 2100) + "\nSkills\nC#", false));
        var result = await c.Service.ParseAsync(c.UserId, stream, "cv.docx", ResumeParseMode.Ai);
        Assert.Equal(ResumeParseMode.Ai, result.Data!.ModeUsed);
        Assert.Equal(2000, result.Data.Summary!.Length);
        Assert.Contains(result.Data.Warnings, w => w.Contains("Summary") && w.Contains("shortened"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public async Task ExplicitInvalidAiIds_AreNotNameOnlySuggestions(int id)
    {
        await using var c = await ResumeParserTestContext.CreateAsync(true);
        c.Handler.Respond = _ => Task.FromResult(RecordingHttpHandler.Json($"{{\"skills\":[{{\"name\":\"C#\",\"id\":{id}}}]}}"));
        using var stream = new MemoryStream(ResumeDocumentFactory.Docx("Skills: C#"));
        var result = await c.Service.ParseAsync(c.UserId, stream, "cv.docx", ResumeParseMode.Ai);
        Assert.True(result.Success);
        Assert.Empty(result.Data!.Skills);
        Assert.NotEmpty(result.Data.Warnings);
    }

    [Fact]
    public async Task HttpCandidateATakesPrecedenceOverCircuitCandidateB()
    {
        await using var c = await ResumeParserTestContext.CreateAsync();
        var other = await c.Fixture.SeedUserAsync("other.fake@example.test");
        c.Fixture.Db.Candidates.Add(new() { UserId = other.Id, FullName = "Other Fake Candidate" });
        await c.Fixture.Db.SaveChangesAsync();
        c.Fixture.HttpContextAccessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        { User = ResumeParserTestContext.Principal(c.UserId, hotjob.Data.AppRoles.Candidate) };
        c.Fixture.AuthStateProvider.SignIn(ResumeParserTestContext.Principal(other.Id, hotjob.Data.AppRoles.Candidate));
        using var file = new MemoryStream(ResumeDocumentFactory.Docx("Skills: C#"));
        var spoofed = await c.Service.ParseAsync(other.Id, file, "cv.docx", ResumeParseMode.Ai);
        Assert.Equal(hotjob.Services.Common.ServiceErrorType.Unauthorized, spoofed.ErrorType);
        Assert.Equal(0, file.Position);
        Assert.Equal(0, c.Handler.RequestCount);
        Assert.True((await c.Service.ParseAsync(c.UserId, file, "cv.docx")).Success);
    }

    [Fact]
    public async Task FullAiSchema_AndDepthToolCallTruncationGuards()
    {
        using var handler = new RecordingHttpHandler { Respond = _ => Task.FromResult(RecordingHttpHandler.Json("""
            {"educations":[{"schoolName":"Example University","degree":"Bachelor","fieldOfStudy":"CS",
            "startDate":"2018-09-01","endDate":"2021-06-30","gpa":3.5,"description":"Example education"}],
            "workExperiences":[{"companyName":"Example Company","jobTitle":"Developer","startDate":"2021-07-01",
            "endDate":null,"isCurrent":true,"description":"Example work"}],"skills":[{"id":1}]}
            """)) };
        var result = await DeepSeekResumeClientTests.Client(handler).ParseAsync("u", "Skills: C#", default);
        Assert.True(result.Success);
        Assert.Equal(3.5, Assert.Single(result.Data!.Educations).Value.Gpa);
        Assert.True(Assert.Single(result.Data.WorkExperiences).IsCurrent);
        Assert.Equal(new DateOnly(2021, 7, 1), result.Data.WorkExperiences[0].Value.StartDate);
        var tooDeep = "{\"skills\":[],\"extra\":" + new string('[', 17) + "0" + new string(']', 17) + "}";
        foreach (var body in new[] { tooDeep, "{\"skills\":[]} /*comment*/" })
        {
            using var invalid = new RecordingHttpHandler { Respond = _ => Task.FromResult(RecordingHttpHandler.Json(body)) };
            Assert.False((await DeepSeekResumeClientTests.Client(invalid).ParseAsync("u", "Skills: C#", default)).Success);
        }
        using var truncated = new RecordingHttpHandler { Respond = _ => Task.FromResult(RecordingHttpHandler.Json("{\"skills\":[]}", "length")) };
        Assert.False((await DeepSeekResumeClientTests.Client(truncated).ParseAsync("u", "Skills: C#", default)).Success);
        using var tools = new RecordingHttpHandler { Respond = _ => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"{\"skills\":[]}","tool_calls":[{"type":"function","function":{"name":"save"}}]}}]}""")
        }) };
        Assert.False((await DeepSeekResumeClientTests.Client(tools).ParseAsync("u", "Skills: C#", default)).Success);
    }
}
