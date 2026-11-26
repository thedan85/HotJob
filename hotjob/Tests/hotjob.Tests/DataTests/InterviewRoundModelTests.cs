using hotjob.Models.Application;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.DataTests;

/// <summary>Database default must match the 1-based round invariant for existing rows.</summary>
public sealed class InterviewRoundModelTests
{
    [Fact]
    public async Task InterviewRound_HasDatabaseDefaultOne()
    {
        await using var fx = await TestFixture.CreateAsync();

        var property = fx.Db.Model.FindEntityType(typeof(Interview))!
            .FindProperty(nameof(Interview.Round))!;

        Assert.Equal(1, property.GetDefaultValue());
    }
}
