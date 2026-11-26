using hotjob.Models.ML;
using hotjob.Services.Admin;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.AdminTests;

public sealed class AdminTrainingLogTests
{
    [Fact]
    public async Task GetTrainingLogs_ReturnsNewestFirstWithTrainer()
    {
        await using var fx = await TestFixture.CreateAsync();
        var trainer = await fx.SeedUserAsync("trainer@example.com", "Str0ng!Pass", "Admin");

        fx.Db.ModelTrainingLogs.AddRange(
            new ModelTrainingLog
            {
                RecordCount = 10,
                AccuracyScore = 0.7,
                TrainedByUserId = trainer.Id,
                TrainedAt = new DateTime(2026, 10, 1),
                IsActive = false
            },
            new ModelTrainingLog
            {
                RecordCount = 20,
                AccuracyScore = 0.8,
                TrainedAt = new DateTime(2026, 11, 1),
                IsActive = true
            });
        await fx.Db.SaveChangesAsync();

        var service = fx.CreateAdminService();
        var result = await service.GetTrainingLogsAsync();

        Assert.True(result.Success);
        var logs = result.Data!;
        Assert.Equal(2, logs.Count);
        Assert.True(logs[0].IsActive);
        Assert.Equal(20, logs[0].RecordCount);
        Assert.Null(logs[0].TrainedByEmail);
        Assert.Equal("trainer@example.com", logs[1].TrainedByEmail);
    }
}
