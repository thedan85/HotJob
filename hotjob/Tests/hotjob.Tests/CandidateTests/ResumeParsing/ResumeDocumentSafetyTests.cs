using System.IO.Compression;
using System.Text;
using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support.ResumeParsing;
using UglyToad.PdfPig;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ResumeDocumentSafetyTests
{
    [Fact]
    public async Task RealPasswordProtectedAndImageOnlyPdf_AreFriendly()
    {
        var protectedPdf = SpecialPdfFactory.PasswordProtected("fixture-password");
        using (var readable = PdfDocument.Open(protectedPdf, new ParsingOptions { Password = "fixture-password" }))
            Assert.Contains("Skills", readable.GetPage(1).Text);
        var imagePdf = SpecialPdfFactory.ImageOnly();
        using (var readable = PdfDocument.Open(imagePdf))
            Assert.NotEmpty(readable.GetPage(1).GetImages());
        foreach (var bytes in new[] { protectedPdf, imagePdf })
        {
            var result = await new ResumeExtractionRunner(TimeProvider.System)
                .RunAsync(ct => new PdfResumeTextExtractor().Extract(bytes, ct), default);
            Assert.False(result.Success);
            Assert.Contains("manually", string.Join(" ", result.Errors));
        }
    }
    [Fact]
    public async Task PdfCharacterCap()
    {
        var bytes = ResumeDocumentFactory.Pdf(new string('A', 105_000));
        var result = await new ResumeExtractionRunner(TimeProvider.System)
            .RunAsync(ct => new PdfResumeTextExtractor().Extract(bytes, ct), default);
        Assert.True(result.Success);
        Assert.Equal(100_000, result.Data!.Text.Length);
        Assert.NotEmpty(result.Data.Warnings);
    }
    [Theory]
    [InlineData(11, 1)]
    [InlineData(9, 3)]
    public void ExpandedArchiveLimits_AreEnforced(int megabytes, int entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open())) writer.Write("<x/>");
            var data = new byte[megabytes * 1024 * 1024];
            // Compressible enough for 5MB input, but not a >200:1 entry: repeated random 8KB blocks.
            var block = new byte[8192]; new Random(123).NextBytes(block);
            for (var offset = 0; offset < data.Length; offset += block.Length)
                block.AsSpan(0, Math.Min(block.Length, data.Length - offset)).CopyTo(data.AsSpan(offset));
            for (var i = 0; i < entries; i++)
            { using var target = zip.CreateEntry($"binary{i}", CompressionLevel.Optimal).Open(); target.Write(data); }
        }
        Assert.Throws<InvalidDataException>(() => new DocxArchiveGuard().Validate(memory.ToArray(), default));
    }
    [Fact]
    public async Task DocxExternalRelationshipIsNotResolved()
    {
        var bytes = ResumeDocumentFactory.Docx("Skills: C#");
        using var memory = new MemoryStream();
        memory.Write(bytes);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/_rels/document.xml.rels").Open());
            writer.Write("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rExternal" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://never-fetch.example.test/private" TargetMode="External"/></Relationships>""");
        }
        var result = await new ResumeExtractionRunner(TimeProvider.System)
            .RunAsync(ct => new DocxResumeTextExtractor().Extract(memory.ToArray(), ct), default);
        Assert.True(result.Success);
        Assert.Contains("C#", result.Data!.Text);
    }
}
