namespace hotjob.Models.ML;

public class ModelTrainingLog
{
    public int Id { get; set; }
    public int RecordCount { get; set; }
    public double AccuracyScore { get; set; }
    public string? ModelFilePath { get; set; }
    public string? TrainedByUserId { get; set; }
    public DateTime TrainedAt { get; set; } = DateTime.UtcNow;

    public Users.ApplicationUser? TrainedBy { get; set; }
}
