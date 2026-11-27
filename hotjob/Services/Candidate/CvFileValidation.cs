using hotjob.Services.Common;

namespace hotjob.Services.Candidate;

internal sealed record ValidatedCvFile(byte[] Bytes, string Extension);

/// <summary>Shared upload checks. The memory-only path never writes or closes the caller's stream.</summary>
internal static class CvFileValidation
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private const string InvalidSignature = "The selected file is not a valid PDF, DOC or DOCX file.";

    public static ServiceResult<string> ValidateMetadata(string? fileName, long? reportedLength)
    {
        if (reportedLength is <= 0) return Fail("The selected file is empty.");
        if (reportedLength > MaxFileSizeBytes) return Fail("CV files must be 5 MB or smaller.");
        if (string.IsNullOrWhiteSpace(fileName)) return Fail("Choose a file to upload.");
        var name = fileName.Trim();
        if (name.Contains("..", StringComparison.Ordinal) || name.IndexOfAny(['/', '\\']) >= 0
            || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal))
            return Fail("The file name is invalid.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension is ".pdf" or ".doc" or ".docx"
            ? ServiceResult<string>.Ok(extension) : Fail("Only PDF, DOC and DOCX files are supported.");
    }

    public static async Task<byte[]> ReadHeaderAsync(Stream file, CancellationToken ct, bool boundWaiting = false)
    {
        var bytes = new byte[8];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = boundWaiting
                ? await ReadBoundedAsync(file, bytes.AsMemory(count), ct)
                : await file.ReadAsync(bytes.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        return bytes[..count];
    }

    public static ServiceResult ValidateHeader(string extension, ReadOnlySpan<byte> header)
    {
        if (header.IsEmpty) return ServiceResult.Fail(ServiceErrorType.Validation, "The selected file is empty.");
        var valid = extension switch
        {
            ".pdf" => header.StartsWith("%PDF-"u8),
            ".docx" => header.StartsWith(new byte[] { 0x50, 0x4b, 3, 4 })
                || header.StartsWith(new byte[] { 0x50, 0x4b, 5, 6 }),
            ".doc" => header.StartsWith(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }),
            _ => false
        };
        return valid ? ServiceResult.Ok() : ServiceResult.Fail(ServiceErrorType.Validation, InvalidSignature);
    }

    public static async Task<ServiceResult<ValidatedCvFile>> ReadValidatedAsync(Stream file, string? fileName, CancellationToken ct)
    {
        if (file is null) return Bad("Choose a file to upload.");
        var metadata = ValidateMetadata(fileName, null);
        if (!metadata.Success) return Bad(metadata.Errors[0]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var header = await ReadHeaderAsync(file, linked.Token, boundWaiting: true);
            var check = ValidateHeader(metadata.Data!, header);
            if (!check.Success) return Bad(check.Errors[0]);
            using var memory = new MemoryStream();
            memory.Write(header);
            var buffer = new byte[81920];
            while (true)
            {
                var remaining = (int)Math.Min(buffer.Length, MaxFileSizeBytes + 1 - memory.Length);
                var count = await ReadBoundedAsync(file, buffer.AsMemory(0, remaining), linked.Token);
                if (count == 0) break;
                memory.Write(buffer, 0, count);
                if (memory.Length > MaxFileSizeBytes) return Bad("CV files must be 5 MB or smaller.");
            }
            return ServiceResult<ValidatedCvFile>.Ok(new(memory.ToArray(), metadata.Data!));
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or NotSupportedException or ObjectDisposedException)
        {
            return Bad("We could not read the file. Please try again or fill in manually.");
        }
    }

    private static ServiceResult<string> Fail(string message) => ServiceResult<string>.Fail(ServiceErrorType.Validation, message);
    private static ServiceResult<ValidatedCvFile> Bad(string message) => ServiceResult<ValidatedCvFile>.Fail(ServiceErrorType.Validation, message);

    private static async Task<int> ReadBoundedAsync(Stream file, Memory<byte> destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var reading = file.ReadAsync(destination, ct).AsTask();
        try { return await reading.WaitAsync(ct); }
        catch
        {
            // Do not close a caller-owned stream or release its outstanding destination.
            _ = reading.ContinueWith(t =>
            {
                _ = t.Exception;
                GC.KeepAlive(destination);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
}
