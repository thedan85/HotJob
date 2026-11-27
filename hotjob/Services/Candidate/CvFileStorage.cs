using System.Globalization;
using hotjob.Services.Common;
using Microsoft.Extensions.Logging;

namespace hotjob.Services.Candidate;

/// <summary>
/// Saves CVs under <c>{root}/{candidateId}/{guid}.{ext}</c>. The stored name is
/// generated, so the original file name can never influence the destination path.
/// </summary>
public sealed class CvFileStorage(string rootPath, ILogger<CvFileStorage> logger) : ICvFileStorage
{
    /// <summary>Largest CV accepted, in bytes (5 MB).</summary>
    public const long MaxFileSizeBytes = CvFileValidation.MaxFileSizeBytes;

    /// <summary>Public URL prefix for every stored CV.</summary>
    public const string PublicPrefix = "/uploads/cv/";

    private readonly string fullRoot = Path.GetFullPath(rootPath);

    public async Task<ServiceResult<string>> SaveAsync(
        int candidateId,
        string? fileName,
        long length,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (candidateId <= 0)
            return ServiceResult<string>.Fail(ServiceErrorType.Validation, "Choose a candidate first.");
        if (content is null)
            return ServiceResult<string>.Fail(ServiceErrorType.Validation, "Choose a file to upload.");
        if (length <= 0)
            return ServiceResult<string>.Fail(ServiceErrorType.Validation, "The selected file is empty.");
        if (length > MaxFileSizeBytes)
            return ServiceResult<string>.Fail(ServiceErrorType.Validation, "CV files must be 5 MB or smaller.");

        var validation = CvFileValidation.ValidateMetadata(fileName, length);
        if (!validation.Success)
            return ServiceResult<string>.Fail(validation.ErrorType!.Value, validation.Errors);

        var extension = validation.Data!;

        var header = await CvFileValidation.ReadHeaderAsync(content, cancellationToken);
        var headerLength = header.Length;
        var signature = CvFileValidation.ValidateHeader(extension, header);
        if (!signature.Success)
            return ServiceResult<string>.Fail(signature.ErrorType!.Value, signature.Errors);

        var directory = Path.Combine(fullRoot, candidateId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);

        var safeName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(directory, safeName);
        var relativeUrl = $"{PublicPrefix}{candidateId.ToString(CultureInfo.InvariantCulture)}/{safeName}";

        var written = (long)headerLength;
        try
        {
            await using var target = new FileStream(
                fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await target.WriteAsync(header.AsMemory(0, headerLength), cancellationToken);
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written += read;
                if (written > MaxFileSizeBytes)
                {
                    await target.DisposeAsync();
                    File.Delete(fullPath);
                    return ServiceResult<string>.Fail(ServiceErrorType.Validation, "CV files must be 5 MB or smaller.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            throw;
        }

        if (written == 0)
        {
            File.Delete(fullPath);
            return ServiceResult<string>.Fail(ServiceErrorType.Validation, "The selected file is empty.");
        }

        return ServiceResult<string>.Ok(relativeUrl);
    }

    public Task DeleteAsync(string? relativeUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)
            || !relativeUrl.StartsWith(PublicPrefix, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var relative = relativeUrl[PublicPrefix.Length..];
        if (relative.Contains("..", StringComparison.Ordinal))
            return Task.CompletedTask;

        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            return Task.CompletedTask;

        if (!File.Exists(fullPath))
            return Task.CompletedTask;

        try
        {
            File.Delete(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // ponytail: the database row is already correct; a stale file must not turn a delete into an error.
            logger.LogWarning("Could not delete a stored CV file; the saved change remains successful.");
        }

        return Task.CompletedTask;
    }

}
