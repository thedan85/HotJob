using hotjob.Services.Candidate;
using hotjob.Services.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.CandidateTests;

/// <summary>
/// CV upload validation and safe storage: extension allow-list, 5 MB limit,
/// path-traversal rejection, generated names, and deletion inside the root only.
/// </summary>
public sealed class CvFileStorageTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "hotjob-cv-tests", Guid.NewGuid().ToString("N"));

    private static CvFileStorage NewStorage(string root) =>
        new(root, NullLogger<CvFileStorage>.Instance);

    private static MemoryStream PdfContent(string text = "%PDF-1.4 test") =>
        new(System.Text.Encoding.ASCII.GetBytes(text));

    [Fact]
    public async Task SaveAsync_ValidPdf_StoresGeneratedFileAndReturnsRelativeUrl()
    {
        var root = NewRoot();
        try
        {
            var storage = NewStorage(root);
            await using var content = PdfContent();

            var result = await storage.SaveAsync(7, "Alex Nguyen CV.pdf", content.Length, content);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.StartsWith("/uploads/cv/7/", result.Data);
            Assert.EndsWith(".pdf", result.Data);
            Assert.DoesNotContain("Alex", result.Data, StringComparison.OrdinalIgnoreCase);

            var stored = Path.Combine(root, result.Data!["/uploads/cv/".Length..]);
            Assert.True(File.Exists(stored));
            Assert.Equal("%PDF-1.4 test", await File.ReadAllTextAsync(stored));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsPdfExtensionWithNonPdfContent()
    {
        var root = NewRoot();
        try
        {
            await using var content = new MemoryStream("MZ not a pdf"u8.ToArray());
            var result = await NewStorage(root).SaveAsync(1, "cv.pdf", content.Length, content);

            Assert.False(result.Success);
            Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
            Assert.Contains("not a valid", Assert.Single(result.Errors));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("cv.exe")]
    [InlineData("cv.txt")]
    [InlineData("cv")]
    [InlineData("cv.pdf.exe")]
    public async Task SaveAsync_RejectsUnsupportedExtensions(string fileName)
    {
        var root = NewRoot();
        try
        {
            await using var content = PdfContent();
            var result = await NewStorage(root).SaveAsync(1, fileName, content.Length, content);

            Assert.False(result.Success);
            Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
            Assert.Contains("PDF, DOC and DOCX", Assert.Single(result.Errors));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../evil.pdf")]
    [InlineData("..\\evil.pdf")]
    [InlineData("/etc/passwd.pdf")]
    [InlineData("nested/evil.pdf")]
    public async Task SaveAsync_RejectsPathTraversalNames(string fileName)
    {
        var root = NewRoot();
        var outside = Path.Combine(Path.GetDirectoryName(root)!, "escaped.pdf");
        try
        {
            await using var content = PdfContent();
            var result = await NewStorage(root).SaveAsync(1, fileName, content.Length, content);

            Assert.False(result.Success);
            Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
            Assert.Contains("file name is invalid", string.Join(" ", result.Errors));
            Assert.False(File.Exists(outside));
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsFilesOverFiveMegabytes()
    {
        var root = NewRoot();
        try
        {
            await using var content = PdfContent();
            var result = await NewStorage(root).SaveAsync(
                1, "cv.pdf", CvFileStorage.MaxFileSizeBytes + 1, content);

            Assert.False(result.Success);
            Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
            Assert.Contains("5 MB", Assert.Single(result.Errors));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsStreamThatExceedsLimitDespiteSmallReportedLength()
    {
        var root = NewRoot();
        try
        {
            await using var content = new MemoryStream(new byte[CvFileStorage.MaxFileSizeBytes + 1]);
            var result = await NewStorage(root).SaveAsync(1, "cv.pdf", 1024, content);

            Assert.False(result.Success);
            Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
            var files = Directory.Exists(Path.Combine(root, "1"))
                ? Directory.GetFiles(Path.Combine(root, "1"))
                : Array.Empty<string>();
            Assert.Empty(files);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesStoredFile()
    {
        var root = NewRoot();
        try
        {
            var storage = NewStorage(root);
            await using (var content = PdfContent())
            {
                var saved = await storage.SaveAsync(2, "cv.pdf", content.Length, content);
                Assert.True(saved.Success);
                var stored = Path.Combine(root, saved.Data!["/uploads/cv/".Length..]);
                Assert.True(File.Exists(stored));

                await storage.DeleteAsync(saved.Data);
                Assert.False(File.Exists(stored));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_IgnoresUrlsThatEscapeTheUploadRoot()
    {
        var root = NewRoot();
        var outside = Path.Combine(Path.GetDirectoryName(root)!, $"escaped-{Guid.NewGuid():N}.pdf");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(outside, "keep me");

            await NewStorage(root).DeleteAsync(
                $"/uploads/cv/../{Path.GetFileName(outside)}");

            Assert.True(File.Exists(outside));
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
