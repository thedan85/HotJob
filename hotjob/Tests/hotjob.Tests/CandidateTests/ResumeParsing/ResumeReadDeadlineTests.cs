using hotjob.Services.Candidate;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ResumeReadDeadlineTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationBoundsWaiting_EvenWhenStreamIgnoresToken(bool blocksHeader)
    {
        using var source = new IgnoringCancellationStream(blocksHeader);
        using var cancel = new CancellationTokenSource();
        var parsing = CvFileValidation.ReadValidatedAsync(source, "cv.pdf", cancel.Token);
        await source.Started.Task;
        cancel.Cancel();
        try
        {
            var completed = await Task.WhenAny(parsing, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(parsing, completed);
            Assert.False((await parsing).Success);
            Assert.False(source.WasDisposed);
            // A delayed producer still owns a valid writable destination after the caller returned.
            source.Destination.Span.Fill(7);
        }
        finally { source.Release.TrySetResult(0); }
    }

    private sealed class IgnoringCancellationStream(bool blocksHeader) : Stream
    {
        private bool headerReturned;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Memory<byte> Destination { get; private set; }
        public bool WasDisposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!blocksHeader && !headerReturned)
            {
                headerReturned = true;
                "%PDF-1.4"u8.CopyTo(buffer.Span);
                return ValueTask.FromResult(8);
            }
            Destination = buffer;
            Started.TrySetResult();
            return new(Release.Task);
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
