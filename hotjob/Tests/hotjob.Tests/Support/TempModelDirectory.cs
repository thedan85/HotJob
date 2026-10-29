using hotjob.Services.ML;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.Support;

/// <summary>
/// Isolated model location for ML tests. Never points at the application's real
/// <c>App_Data</c>; the directory is removed after the test.
/// </summary>
public sealed class TempModelDirectory : IDisposable
{
    private readonly string root;

    public TempModelDirectory()
    {
        root = Path.Combine(Path.GetTempPath(), "hotjob-ml-tests", Guid.NewGuid().ToString("N"));
        ModelPath = Path.Combine(root, "models", "matchscore.zip");
    }

    public string ModelPath { get; }

    public MatchScoreOptions CreateOptions(int minTrainingRecords = 50) => new()
    {
        ModelPath = ModelPath,
        MinTrainingRecords = minTrainingRecords
    };

    public MatchModelStore CreateStore() => new(
        Options.Create(CreateOptions()),
        root,
        NullLogger<MatchModelStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
