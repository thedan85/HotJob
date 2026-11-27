using System.Text;
using hotjob.Services.Candidate;
using hotjob.Services.Common;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class CvFileValidationTests
{
    [Theory]
    [InlineData("cv.exe", "%PDF-test", "Only PDF, DOC and DOCX files are supported.")]
    [InlineData("../cv.pdf", "%PDF-test", "The file name is invalid.")]
    [InlineData("cv.pdf", "MZ", "The selected file is not a valid PDF, DOC or DOCX file.")]
    [InlineData("cv.pdf", "", "The selected file is empty.")]
    public async Task InvalidFiles_UseUploadErrors(string name, string text, string error)
    {
        using var source = new MemoryStream(Encoding.ASCII.GetBytes(text));
        var result = await CvFileValidation.ReadValidatedAsync(source, name, default);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Equal(error, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task NonSeekableOneByteReads_RemainCallerOwned()
    {
        using var stream = new ChunkStream("%PDF-1.4 fake"u8.ToArray());
        var result = await CvFileValidation.ReadValidatedAsync(stream, "cv.PDF", default);
        Assert.True(result.Success);
        Assert.Equal(".pdf", result.Data!.Extension);
        Assert.Equal("%PDF-1.4 fake", Encoding.ASCII.GetString(result.Data.Bytes));
        Assert.False(stream.WasDisposed);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ActualByteLimit_IsEnforced(int extra, bool accepted)
    {
        var bytes = new byte[5 * 1024 * 1024 + extra];
        "%PDF-"u8.CopyTo(bytes);
        using var stream = new MemoryStream(bytes);
        var result = await CvFileValidation.ReadValidatedAsync(stream, "cv.pdf", default);
        Assert.Equal(accepted, result.Success);
        if (!accepted) Assert.Contains("5 MB", Assert.Single(result.Errors));
    }

    [Fact]
    public async Task ReadStartsAtCurrentOffset_AndCancellationIsFriendly()
    {
        using var source = new MemoryStream("xx%PDF-test"u8.ToArray());
        source.Position = 2;
        Assert.True((await CvFileValidation.ReadValidatedAsync(source, "cv.pdf", default)).Success);
        Assert.False((await CvFileValidation.ReadValidatedAsync(source, "cv.pdf", new(true))).Success);
    }

    private sealed class ChunkStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public bool WasDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            inner.ReadAsync(buffer[..Math.Min(1, buffer.Length)], ct);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(1, count));
        protected override void Dispose(bool disposing) { WasDisposed = true; inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
