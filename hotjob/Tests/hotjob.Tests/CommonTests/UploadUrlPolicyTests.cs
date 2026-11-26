using hotjob.Services.Common;

namespace hotjob.Tests.CommonTests;

/// <summary>Snapshot links must open real uploads without becoming an XSS or traversal vector.</summary>
public sealed class UploadUrlPolicyTests
{
    [Theory]
    [InlineData("/uploads/cv/1/8f14e45fceea167a5a36dedd4bea2543.pdf")]
    [InlineData("https://cdn.example.com/uploads/cv/1/resume.pdf")]
    [InlineData("http://localhost/uploads/cv/1/resume.docx")]
    public void IsSafeSnapshotUrl_AcceptsUploadsAndHttpLinks(string url) =>
        Assert.True(UploadUrlPolicy.IsSafeSnapshotUrl(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example/uploads/cv/1/resume.pdf")]
    [InlineData("/uploads/cv/../secret.pdf")]
    [InlineData("/uploads/cv/1/resume.pdf?download=1")]
    [InlineData("/uploads/cv/1/resume.pdf#page=2")]
    [InlineData(@"\\server\share\resume.pdf")]
    public void IsSafeSnapshotUrl_RejectsHostileUrls(string? url) =>
        Assert.False(UploadUrlPolicy.IsSafeSnapshotUrl(url));
}
