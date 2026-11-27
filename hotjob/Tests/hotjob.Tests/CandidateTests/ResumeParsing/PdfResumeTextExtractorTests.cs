using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support.ResumeParsing;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class PdfResumeTextExtractorTests
{
    [Fact]
    public async Task ExtractsTextAndCapsPages()
    {
        var pages = Enumerable.Repeat("Education\nExample University", 15).Append("OMITTED_PAGE_SENTINEL").ToArray();
        var result = await new ResumeExtractionRunner(TimeProvider.System)
            .RunAsync(ct => new PdfResumeTextExtractor().Extract(ResumeDocumentFactory.Pdf(pages), ct), default);
        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Contains("Education", result.Data!.Text);
        Assert.DoesNotContain("OMITTED_PAGE_SENTINEL", result.Data.Text);
        Assert.NotEmpty(result.Data.Warnings);
    }
    [Fact]
    public async Task BlankAndCorruptPdf_ReturnFriendlyErrors()
    {
        foreach (var bytes in new[] { ResumeDocumentFactory.Pdf(""), "%PDF-corrupt"u8.ToArray() })
        {
            var result = await new ResumeExtractionRunner(TimeProvider.System)
                .RunAsync(ct => new PdfResumeTextExtractor().Extract(bytes, ct), default);
            Assert.False(result.Success);
            Assert.Contains("manually", string.Join(" ", result.Errors));
        }
    }
    [Fact]
    public async Task Runner_TimeoutRetainsSlotsUntilRealCompletion()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var runner = new ResumeExtractionRunner(clock);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        ExtractedResumeText Work(CancellationToken ct)
        {
            if (Interlocked.Increment(ref count) == 2) started.TrySetResult();
            release.Wait();
            return new("text", []);
        }
        var a = runner.RunAsync(Work, default);
        var b = runner.RunAsync(Work, default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.False((await a).Success);
            Assert.False((await b).Success);
            Assert.False((await runner.RunAsync(_ => new("unexpected", []), default)).Success);
            Assert.Equal(2, count);
        }
        finally { release.Set(); }
    }
}
