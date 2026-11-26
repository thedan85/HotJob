using System.Text.Json;
using hotjob.Models.Application;
using hotjob.Services.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Tests.CandidateTests;

public sealed partial class ResumeServiceTests
{
    [Theory]
    [InlineData("json")]
    [InlineData("escaped-json")]
    [InlineData("camel-case-json")]
    [InlineData("malformed")]
    [InlineData("unknown-shape")]
    [InlineData("missing-file-property")]
    [InlineData("invalid-file-property")]
    [InlineData("blank-json")]
    public async Task UploadFileAsync_KeepsOldFileForJsonReferencesOrUncertainSnapshots(string snapshotKind)
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "replace-json@example.test");
        var service = NewService(fx, files.Root);
        var created = await service.CreateAsync(userId, await SimpleInputAsync(fx, "CV"));
        var originalUrl = await UploadPdfAsync(service, userId, created.Data!.Id);
        await AddSnapshotAsync(fx, candidateId, created.Data.Id, originalUrl, snapshotKind);

        await using var content = new MemoryStream("%PDF-1.4 replacement"u8.ToArray());
        var result = await service.UploadFileAsync(userId, created.Data.Id, "new.pdf", content.Length, content);

        Assert.True(result.Success);
        Assert.NotEqual(originalUrl, result.Data!.FileUrl);
        Assert.True(File.Exists(files.PathFor(originalUrl)));
        Assert.True(File.Exists(files.PathFor(result.Data.FileUrl!)));
    }

    [Fact]
    public async Task RemoveResumeFileAsync_OwnerClearsFileButKeepsResumeAndContent()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "remove@example.test");
        var skill = await SeedSkillAsync(fx, "C#");
        var service = NewService(fx, files.Root);
        var created = await service.CreateAsync(userId, FullInput(skill.Id));
        var url = await UploadPdfAsync(service, userId, created.Data!.Id);

        var result = await service.RemoveResumeFileAsync(userId, created.Data.Id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Null(result.Data!.FileUrl);
        Assert.Equal(created.Data.Title, result.Data.Title);
        Assert.Equal(created.Data.Summary, result.Data.Summary);
        Assert.Equal(created.Data.Skills, result.Data.Skills);
        Assert.Equal(created.Data.Educations, result.Data.Educations);
        Assert.Equal(created.Data.WorkExperiences, result.Data.WorkExperiences);
        var stored = await fx.Db.Resumes.AsNoTracking().SingleAsync();
        Assert.Equal(created.Data.Id, stored.Id);
        Assert.Equal(candidateId, stored.CandidateId);
        Assert.True(stored.IsPrimary);
        Assert.Equal(created.Data.CreatedAt, stored.CreatedAt);
        Assert.Null(stored.FileUrl);
        Assert.False(File.Exists(files.PathFor(url)));
    }

    [Fact]
    public async Task RemoveResumeFileAsync_NonOwnerCannotChangeResumeOrDeleteFile()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (owner, _) = await SeedCandidateAsync(fx, "owner-remove@example.test");
        var (other, _) = await SeedCandidateAsync(fx, "other-remove@example.test");
        var service = NewService(fx, files.Root);
        var created = await service.CreateAsync(owner, await SimpleInputAsync(fx, "Private CV"));
        var url = await UploadPdfAsync(service, owner, created.Data!.Id);

        var result = await service.RemoveResumeFileAsync(other, created.Data.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
        Assert.Equal("Resume not found.", Assert.Single(result.Errors));
        Assert.Equal(url, (await fx.Db.Resumes.AsNoTracking().SingleAsync()).FileUrl);
        Assert.True(File.Exists(files.PathFor(url)));
    }

    [Fact]
    public async Task RemoveResumeFileAsync_AnonymousUserIsUnauthorized()
    {
        await using var fx = await TestFixture.CreateAsync();
        var result = await NewService(fx).RemoveResumeFileAsync(null, 1);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
    }

    [Fact]
    public async Task RemoveResumeFileAsync_MissingResumeIsNotFound()
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, _) = await SeedCandidateAsync(fx, "missing-remove@example.test");
        var result = await NewService(fx).RemoveResumeFileAsync(userId, 999);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.NotFound, result.ErrorType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveResumeFileAsync_NoFileIsCleanIdempotentSuccessEvenWithoutSkills(bool withSkills)
    {
        await using var fx = await TestFixture.CreateAsync();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "no-file@example.test");
        var service = NewService(fx);
        var created = await service.CreateAsync(userId, await SimpleInputAsync(fx, "CV"));
        if (!withSkills)
        {
            fx.Db.ResumeSkills.RemoveRange(fx.Db.ResumeSkills);
            await fx.Db.SaveChangesAsync();
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await service.RemoveResumeFileAsync(userId, created.Data!.Id);
            Assert.True(result.Success);
            Assert.Empty(result.Errors);
            Assert.Null(result.Data!.FileUrl);
        }
        Assert.Equal(candidateId, (await fx.Db.Resumes.AsNoTracking().SingleAsync()).CandidateId);
    }

    [Theory]
    [InlineData("column", true)]
    [InlineData("json", true)]
    [InlineData("escaped-json", true)]
    [InlineData("camel-case-json", true)]
    [InlineData("malformed", true)]
    [InlineData("unknown-shape", true)]
    [InlineData("missing-file-property", true)]
    [InlineData("invalid-file-property", true)]
    [InlineData("blank-json", true)]
    [InlineData("unrelated", false)]
    public async Task RemoveResumeFileAsync_PreservesSnapshotsAndOnlyDeletesKnownUnreferencedFiles(
        string snapshotKind, bool keepFile)
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "snapshot-remove@example.test");
        var service = NewService(fx, files.Root);
        var created = await service.CreateAsync(userId, await SimpleInputAsync(fx, "CV"));
        var url = await UploadPdfAsync(service, userId, created.Data!.Id);
        // Use a different resume to ensure reference checks are global, not limited to ResumeId.
        var applied = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Applied CV"));
        var snapshot = await AddSnapshotAsync(fx, candidateId, applied.Data!.Id, url, snapshotKind);
        var originalJson = snapshot.SnapshotJson;
        var originalSnapshotUrl = snapshot.SnapshotFileUrl;

        var result = await service.RemoveResumeFileAsync(userId, created.Data.Id);

        Assert.True(result.Success);
        Assert.Null((await fx.Db.Resumes.AsNoTracking().SingleAsync(row => row.Id == created.Data.Id)).FileUrl);
        Assert.Equal(keepFile, File.Exists(files.PathFor(url)));
        var unchanged = await fx.Db.Applications.AsNoTracking().SingleAsync();
        Assert.Equal(originalJson, unchanged.SnapshotJson);
        Assert.Equal(originalSnapshotUrl, unchanged.SnapshotFileUrl);
        if (keepFile)
            Assert.Equal("%PDF-1.4 attachment", await File.ReadAllTextAsync(files.PathFor(url)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveResumeFileAsync_ExistingAttachmentRequiresSavedSkill(bool hasOtherContent)
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, _) = await SeedCandidateAsync(fx, "empty-remove@example.test");
        var skill = await SeedSkillAsync(fx, "SQL");
        var service = NewService(fx, files.Root);
        var input = hasOtherContent ? FullInput(skill.Id) : await SimpleInputAsync(fx, "File-only legacy CV");
        var created = await service.CreateAsync(userId, input);
        var url = await UploadPdfAsync(service, userId, created.Data!.Id);
        fx.Db.ResumeSkills.RemoveRange(fx.Db.ResumeSkills);
        await fx.Db.SaveChangesAsync();

        var result = await service.RemoveResumeFileAsync(userId, created.Data.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Equal("Add and save at least one skill before removing the attached file.", Assert.Single(result.Errors));
        Assert.Equal(url, (await fx.Db.Resumes.AsNoTracking().SingleAsync()).FileUrl);
        Assert.True(File.Exists(files.PathFor(url)));
    }

    [Fact]
    public async Task RemoveResumeFileAsync_KeepsFileStillAttachedToAnotherResume()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, _) = await SeedCandidateAsync(fx, "shared-remove@example.test");
        var service = NewService(fx, files.Root);
        var first = await service.CreateAsync(userId, await SimpleInputAsync(fx, "First"));
        var url = await UploadPdfAsync(service, userId, first.Data!.Id);
        var second = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Second"));
        (await fx.Db.Resumes.FindAsync(second.Data!.Id))!.FileUrl = url;
        await fx.Db.SaveChangesAsync();

        var result = await service.RemoveResumeFileAsync(userId, first.Data.Id);

        Assert.True(result.Success);
        Assert.True(File.Exists(files.PathFor(url)));
        Assert.Equal(url, (await service.GetAsync(userId, second.Data.Id)).Data!.FileUrl);
    }

    [Fact]
    public async Task RemoveResumeFileAsync_AppliedResumeKeepsItsSnapshotFileAndStillCannotBeDeleted()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "applied-remove@example.test");
        var service = NewService(fx, files.Root);
        var created = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Applied CV"));
        var url = await UploadPdfAsync(service, userId, created.Data!.Id);
        var application = await AddSnapshotAsync(fx, candidateId, created.Data.Id, url, "json");
        application.SnapshotFileUrl = url;
        await fx.Db.SaveChangesAsync();
        var originalJson = application.SnapshotJson;

        var removed = await service.RemoveResumeFileAsync(userId, created.Data.Id);
        var deleted = await service.DeleteAsync(userId, created.Data.Id);

        Assert.True(removed.Success);
        Assert.Null(removed.Data!.FileUrl);
        Assert.Equal(ServiceErrorType.Conflict, deleted.ErrorType);
        Assert.True(await fx.Db.Resumes.AsNoTracking().AnyAsync(row => row.Id == created.Data.Id));
        Assert.Equal(originalJson, (await fx.Db.Applications.AsNoTracking().SingleAsync()).SnapshotJson);
        Assert.Equal(url, (await fx.Db.Applications.AsNoTracking().SingleAsync()).SnapshotFileUrl);
        Assert.Equal("%PDF-1.4 attachment", await File.ReadAllTextAsync(files.PathFor(url)));
    }

    [Fact]
    public async Task DeleteAsync_UnappliedResumeKeepsFileReferencedByAnotherApplicationsJson()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, candidateId) = await SeedCandidateAsync(fx, "delete-shared@example.test");
        var service = NewService(fx, files.Root);
        var original = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Original"));
        var url = await UploadPdfAsync(service, userId, original.Data!.Id);
        var applied = await service.CreateAsync(userId, await SimpleInputAsync(fx, "Applied"));
        await AddSnapshotAsync(fx, candidateId, applied.Data!.Id, url, "json");

        var result = await service.DeleteAsync(userId, original.Data.Id);

        Assert.True(result.Success);
        Assert.False(await fx.Db.Resumes.AsNoTracking().AnyAsync(row => row.Id == original.Data.Id));
        Assert.True(File.Exists(files.PathFor(url)));
    }

    [Fact]
    public async Task UploadFileAsync_CleanupFailureAfterSaveKeepsNewAttachmentAndReturnsSuccess()
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, _) = await SeedCandidateAsync(fx, "replace-cleanup@example.test");
        var storage = new CvFileStorage(files.Root, Microsoft.Extensions.Logging.Abstractions.NullLogger<CvFileStorage>.Instance);
        var originalService = NewService(fx, files.Root);
        var created = await originalService.CreateAsync(userId, await SimpleInputAsync(fx, "CV"));
        var url = await UploadPdfAsync(originalService, userId, created.Data!.Id);
        var logger = new CleanupLogger();
        var service = new ResumeService(fx.Db, new FailingCleanupStorage(false, storage), TimeProvider.System, logger);
        await using var replacement = new MemoryStream("%PDF-1.4 replacement"u8.ToArray());

        var result = await service.UploadFileAsync(userId, created.Data.Id, "new.pdf", replacement.Length, replacement);

        Assert.True(result.Success);
        Assert.NotEqual(url, result.Data!.FileUrl);
        Assert.Equal(result.Data.FileUrl, (await fx.Db.Resumes.AsNoTracking().SingleAsync()).FileUrl);
        Assert.True(File.Exists(files.PathFor(url)));
        Assert.True(File.Exists(files.PathFor(result.Data.FileUrl!)));
        Assert.Single(logger.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveResumeFileAsync_CleanupFailureAfterSaveStillReturnsSuccessAndSafeWarning(bool canceled)
    {
        await using var fx = await TestFixture.CreateAsync();
        using var files = new TestCvDirectory();
        var (userId, _) = await SeedCandidateAsync(fx, "cleanup-remove@example.test");
        var originalService = NewService(fx, files.Root);
        var created = await originalService.CreateAsync(userId, await SimpleInputAsync(fx, "CV"));
        var url = await UploadPdfAsync(originalService, userId, created.Data!.Id);
        var logger = new CleanupLogger();
        var service = new ResumeService(fx.Db, new FailingCleanupStorage(canceled), TimeProvider.System, logger);

        var result = await service.RemoveResumeFileAsync(userId, created.Data.Id);

        Assert.True(result.Success);
        Assert.Null(result.Data!.FileUrl);
        Assert.Null((await fx.Db.Resumes.AsNoTracking().SingleAsync()).FileUrl);
        Assert.True(File.Exists(files.PathFor(url)));
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("saved change remains successful", warning);
        Assert.DoesNotContain(url, warning);
        Assert.DoesNotContain("sensitive", warning);
    }

    private sealed class FailingCleanupStorage(bool canceled, ICvFileStorage? inner = null) : ICvFileStorage
    {
        public Task<ServiceResult<string>> SaveAsync(
            int candidateId, string? fileName, long length, Stream content, CancellationToken cancellationToken = default) =>
            inner?.SaveAsync(candidateId, fileName, length, content, cancellationToken)
                ?? throw new NotSupportedException();

        public Task DeleteAsync(string? relativeUrl, CancellationToken cancellationToken = default) =>
            throw (canceled ? new OperationCanceledException("sensitive") : new IOException("sensitive"));
    }

    private sealed class CleanupLogger : ILogger<ResumeService>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
            Assert.Null(exception);
        }
    }

    private static async Task<string> UploadPdfAsync(ResumeService service, string userId, int resumeId)
    {
        await using var content = new MemoryStream("%PDF-1.4 attachment"u8.ToArray());
        var result = await service.UploadFileAsync(userId, resumeId, "cv.pdf", content.Length, content);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return result.Data!.FileUrl!;
    }

    private static async Task<Application> AddSnapshotAsync(
        TestFixture fx, int candidateId, int resumeId, string fileUrl, string kind)
    {
        var snapshot = new Application
        {
            CandidateId = candidateId,
            ResumeId = resumeId,
            JobPostingId = await SeedJobAsync(fx),
            SnapshotFileUrl = kind == "column" ? fileUrl : null,
            SnapshotJson = kind switch
            {
                "json" => JsonSerializer.Serialize(new ResumeSnapshot("Frozen", null, fileUrl, [], [], [])),
                "escaped-json" => JsonSerializer.Serialize(new { FileUrl = fileUrl })
                    .Replace("/", "\\u002F", StringComparison.Ordinal),
                "camel-case-json" => JsonSerializer.Serialize(new { fileUrl }),
                "malformed" => "{broken",
                "unknown-shape" => "[]",
                "missing-file-property" => "{}",
                "invalid-file-property" => "{\"FileUrl\":123}",
                "blank-json" => " ",
                "unrelated" => JsonSerializer.Serialize(new { FileUrl = "/uploads/cv/other.pdf" }),
                _ => null
            }
        };
        fx.Db.Applications.Add(snapshot);
        await fx.Db.SaveChangesAsync();
        return snapshot;
    }

    private sealed class TestCvDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", $"hotjob-task4-{Guid.NewGuid():N}");

        public string PathFor(string url) => Path.Combine(Root, url[CvFileStorage.PublicPrefix.Length..]);

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
