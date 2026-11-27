using System.Net;
using System.Text;
using hotjob.DTOs.Candidate;
using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support.ResumeParsing;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ResumeParsingEdgeTests
{
    [Theory]
    [InlineData("2021-99-99")]
    [InlineData("0000-01-01")]
    [InlineData("13/2021")]
    public void InvalidDates_AreOmitted(string value)
    {
        var result = new ResumeDateParser().ParseRange(value);
        Assert.Null(result.StartDate);
        Assert.NotEmpty(result.Warnings);
    }
    [Fact]
    public void Normalization_IsIdempotentForDegreeOnly_AndUnknownTruncationWarns()
    {
        var normalizer = new ParsedResumeNormalizer();
        var once = normalizer.Normalize(new() { Educations = [
            new(new() { SchoolName = "Example University" }, "Bachelor", null, ResumeParseConfidence.Medium)] }, []);
        var twice = normalizer.Normalize(once, []);
        Assert.Null(twice.Educations[0].FieldOfStudy);
        Assert.Equal("Bachelor", twice.Educations[0].Value.Major);
        var unknown = normalizer.Normalize(new() { UnrecognizedSkills = [new string('z', 101)] }, []);
        Assert.Equal(100, Assert.Single(unknown.UnrecognizedSkills).Length);
        Assert.Contains(unknown.Warnings, w => w.Contains("shortened"));
    }
    [Fact]
    public void SurrogateBoundary_DoesNotEmitInvalidUnicode()
    {
        var result = new ParsedResumeNormalizer().Normalize(new()
        { Summary = new string('a', 1999) + "\U0001F600" }, []);
        Assert.Equal(1999, result.Summary!.Length);
        Assert.True(result.Summary.IsNormalized());
    }
    [Theory]
    [InlineData("http://example.test")]
    [InlineData("https://user:pass@example.test")]
    [InlineData("https://example.test/?key=bad")]
    [InlineData("https://example.test/#fragment")]
    [InlineData("/relative")]
    public void UnsafeBaseUrls_AreRejected(string url) => Assert.False(DeepSeekResumeClient.ValidBaseUrl(url));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedHttpBodyIsRejected_DeclaredOrChunked(bool chunked)
    {
        using var handler = new RecordingHttpHandler { Respond = _ =>
        {
            var bytes = Encoding.UTF8.GetBytes(new string('x', 256 * 1024 + 1));
            HttpContent content = chunked ? new StreamContent(new UnseekableMemoryStream(bytes)) : new ByteArrayContent(bytes);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }};
        var result = await DeepSeekResumeClientTests.Client(handler).ParseAsync("u", "Skills: C#", default);
        Assert.False(result.Success);
        Assert.Equal(1, handler.RequestCount);
    }
    private sealed class UnseekableMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    [Fact]
    public async Task QuotaCountsFailedCalls_AndBlockedCallsMakeNoHttp()
    {
        using var handler = new RecordingHttpHandler { Respond = _ => throw new HttpRequestException("private error") };
        var client = DeepSeekResumeClientTests.Client(handler);
        for (var i = 0; i < 4; i++) Assert.False((await client.ParseAsync("u", "Skills: C#", default)).Success);
        Assert.Equal(3, handler.RequestCount);
    }
}
