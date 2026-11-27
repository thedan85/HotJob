using System.IO.Compression;
using System.Text;
using hotjob.Services.Candidate;
using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support.ResumeParsing;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class DocxResumeTextExtractorTests
{
    private static ResumeTextExtractor Extractor() => new(new ResumeExtractionRunner(TimeProvider.System), new(), new());
    [Fact]
    public async Task VietnameseTextAndCharacterCap()
    {
        var bytes = ResumeDocumentFactory.Docx("Học vấn\nExample University\nKỹ năng\n" + new string('x', 110_000), false);
        var result = await Extractor().ExtractAsync(new(bytes, ".docx"), default);
        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.StartsWith("Học vấn", result.Data!.Text);
        Assert.Equal(100_000, result.Data.Text.Length);
        Assert.NotEmpty(result.Data.Warnings);
    }
    [Theory]
    [InlineData("ratio")]
    [InlineData("entries")]
    [InlineData("path")]
    [InlineData("duplicate")]
    [InlineData("depth")]
    [InlineData("dtd")]
    [InlineData("characters")]
    [InlineData("missing")]
    public async Task UnsafeArchives_AreFriendly(string kind)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Add(string name, string text)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name,
                    kind == "ratio" ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression).Open());
                writer.Write(text);
            }
            if (kind != "missing") Add("word/document.xml", kind switch
            {
                "ratio" => "<x>" + new string('x', 100_000) + "</x>",
                "depth" => string.Concat(Enumerable.Repeat("<x>", 66)) + string.Concat(Enumerable.Repeat("</x>", 66)),
                "dtd" => "<!DOCTYPE x [<!ENTITY a 'blocked'>]><x>&a;</x>",
                "characters" => "<x>" + new string('a', 2_000_001) + "</x>",
                _ => "<x/>"
            });
            if (kind == "path") Add("../bad", "x");
            if (kind == "duplicate") Add("word/document.xml", "<x/>");
            if (kind == "entries") for (var i = 0; i < 257; i++) Add($"a{i}", "x");
        }
        // Exercise the guard directly: OpenXML opening an invalid OPC package is not proof of these guards.
        var violation = Record.Exception(() => new DocxArchiveGuard().Validate(output.ToArray(), default));
        Assert.NotNull(violation);
        var result = await Extractor().ExtractAsync(new(output.ToArray(), ".docx"), default);
        Assert.False(result.Success);
        Assert.Contains("manually", string.Join(" ", result.Errors));
    }
    [Fact]
    public void GuardControl_AcceptsOtherwiseSafeXmlArchive()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml", CompressionLevel.NoCompression).Open());
            writer.Write("<x>safe XML</x>");
        }
        new DocxArchiveGuard().Validate(output.ToArray(), default);
    }
    [Fact]
    public async Task DocAndCorruptFiles_AreManualEntryErrors()
    {
        foreach (var file in new[] { new ValidatedCvFile([], ".doc"), new ValidatedCvFile("PKbad"u8.ToArray(), ".docx") })
            Assert.Contains("manually", string.Join(" ", (await Extractor().ExtractAsync(file, default)).Errors));
    }
}
