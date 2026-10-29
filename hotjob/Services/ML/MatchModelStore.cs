using hotjob.DTOs.ML;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace hotjob.Services.ML;

/// <summary>One training row: the four features plus the hiring outcome label.</summary>
public sealed class MatchTrainingRow
{
    /// <summary>True when the application ended in an offer.</summary>
    public bool Label { get; set; }

    /// <summary>Share of the job's skill weight covered by the resume.</summary>
    public float MatchedWeightRatio { get; set; }

    /// <summary>Share of the job's required skills present on the resume.</summary>
    public float MatchedCountRatio { get; set; }

    /// <summary>Resume skill years minus the job's required experience.</summary>
    public float ExperienceGap { get; set; }

    /// <summary>1 when the resume has every required skill, else 0.</summary>
    public float HasAllRequired { get; set; }
}

/// <summary>Prediction produced by the FastTree binary classifier.</summary>
public sealed class MatchPrediction
{
    /// <summary>Model decision at the default 0.5 threshold.</summary>
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    /// <summary>Probability of the positive (offered) class.</summary>
    public float Probability { get; set; }
}

/// <summary>Thread-safe access to the persisted match-scoring model.</summary>
public interface IMatchModelStore
{
    /// <summary>Absolute path of the configured model file.</summary>
    string ModelFilePath { get; }

    /// <summary>Whether the model file exists and loads successfully.</summary>
    bool IsModelAvailable { get; }

    /// <summary>Predicts the offer probability; false when no usable model is loaded.</summary>
    bool TryPredict(MatchFeatures features, out double probability);

    /// <summary>Atomically replaces the persisted model and makes it the active one in memory.</summary>
    void Save(ITransformer model, DataViewSchema schema);

    /// <summary>Drops the in-memory model so the next call reloads it from disk.</summary>
    void Invalidate();
}

/// <summary>
/// Lazily loads <c>App_Data/models/matchscore.zip</c>. A missing or corrupt file
/// disables model scoring (callers fall back to the weighted rule) without throwing.
/// </summary>
public sealed class MatchModelStore : IMatchModelStore
{
    private readonly MLContext mlContext = new(seed: 1);
    private readonly ILogger<MatchModelStore> logger;
    private readonly object sync = new();
    private ITransformer? model;
    private bool loadAttempted;

    /// <summary>Creates the store, resolving a relative model path against <paramref name="contentRootPath"/>.</summary>
    public MatchModelStore(
        IOptions<MatchScoreOptions> options,
        string contentRootPath,
        ILogger<MatchModelStore> logger)
    {
        this.logger = logger;
        var configured = options.Value.ModelPath;
        ModelFilePath = Path.GetFullPath(
            Path.IsPathRooted(configured) ? configured : Path.Combine(contentRootPath, configured));
    }

    public string ModelFilePath { get; }

    public bool IsModelAvailable
    {
        get
        {
            lock (sync)
            {
                EnsureLoaded();
                return model is not null;
            }
        }
    }

    public bool TryPredict(MatchFeatures features, out double probability)
    {
        lock (sync)
        {
            EnsureLoaded();
            probability = 0;
            if (model is null)
            {
                return false;
            }

            try
            {
                using var engine = mlContext.Model.CreatePredictionEngine<MatchTrainingRow, MatchPrediction>(model);
                var prediction = engine.Predict(new MatchTrainingRow
                {
                    MatchedWeightRatio = (float)features.MatchedWeightRatio,
                    MatchedCountRatio = (float)features.MatchedCountRatio,
                    ExperienceGap = (float)features.ExperienceGap,
                    HasAllRequired = features.HasAllRequired ? 1f : 0f
                });
                probability = prediction.Probability;
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // ponytail: a model that loaded but cannot predict is treated like a corrupt
                // file — log once and let every caller use the weighted rule.
                logger.LogWarning(exception, "Match model at {ModelPath} could not score; using weighted rule.", ModelFilePath);
                model = null;
                return false;
            }
        }
    }

    public void Save(ITransformer trainedModel, DataViewSchema schema)
    {
        lock (sync)
        {
            var directory = Path.GetDirectoryName(ModelFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = $"{ModelFilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = File.Create(temporaryPath))
                {
                    mlContext.Model.Save(trainedModel, schema, stream);
                }

                File.Move(temporaryPath, ModelFilePath, overwrite: true);
                model = trainedModel;
                loadAttempted = true;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    public void Invalidate()
    {
        lock (sync)
        {
            model = null;
            loadAttempted = false;
        }
    }

    private void EnsureLoaded()
    {
        if (loadAttempted)
        {
            return;
        }

        loadAttempted = true;
        if (!File.Exists(ModelFilePath))
        {
            return;
        }

        try
        {
            model = mlContext.Model.Load(ModelFilePath, out _);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Match model at {ModelPath} is unreadable; using weighted rule.", ModelFilePath);
            model = null;
        }
    }
}
