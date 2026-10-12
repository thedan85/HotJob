using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.ML;

/// <summary>Log entry of one ML model training run.</summary>
public class ModelTrainingLog
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int RecordCount { get; set; }

    [Range(0, 1)]
    public double AccuracyScore { get; set; }

    [MaxLength(500)]
    public string? ModelFilePath { get; set; }

    [MaxLength(255)]
    public string? TrainedByUserId { get; set; }

    public DateTime TrainedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who triggered the training run. Null for scheduled runs.</summary>
    public Users.ApplicationUser? TrainedBy { get; set; }
}
