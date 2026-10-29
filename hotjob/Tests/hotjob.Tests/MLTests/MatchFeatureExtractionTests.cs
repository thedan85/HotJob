using hotjob.Services.ML;

namespace hotjob.Tests.MLTests;

/// <summary>Contract D9 feature extraction, including safe division and plan-literal gap.</summary>
public sealed class MatchFeatureExtractionTests
{
    private static readonly IReadOnlyDictionary<int, double> JobSkills = new Dictionary<int, double>
    {
        [1] = 9,
        [2] = 6
    };

    [Fact]
    public void Extract_UsesSharedWeightOverTotalJobWeight()
    {
        var features = MatchFeatureExtractor.Extract(
            JobSkills,
            new Dictionary<int, int> { [1] = 2 },
            experienceRequired: 3);

        Assert.NotNull(features);
        Assert.Equal(0.6, features.MatchedWeightRatio, 10);
        Assert.Equal(0.5, features.MatchedCountRatio, 10);
        Assert.False(features.HasAllRequired);
    }

    [Fact]
    public void Extract_ZeroTotalWeight_UsesCountRatio()
    {
        var features = MatchFeatureExtractor.Extract(
            new Dictionary<int, double> { [1] = 0, [2] = 0, [3] = 0 },
            new Dictionary<int, int> { [1] = 1 },
            experienceRequired: 0);

        Assert.NotNull(features);
        Assert.Equal(1d / 3, features.MatchedWeightRatio, 10);
        Assert.Equal(1d / 3, features.MatchedCountRatio, 10);
    }

    [Fact]
    public void Extract_ExperienceGap_SumsEveryResumeSkillYear()
    {
        var features = MatchFeatureExtractor.Extract(
            JobSkills,
            new Dictionary<int, int> { [1] = 2, [2] = 3, [99] = 4 },
            experienceRequired: 6);

        Assert.NotNull(features);
        Assert.Equal(3, features.ExperienceGap);
    }

    [Fact]
    public void Extract_HasAllRequired_OnlyWhenEveryJobSkillIsPresent()
    {
        var complete = MatchFeatureExtractor.Extract(
            JobSkills,
            new Dictionary<int, int> { [1] = 1, [2] = 1 },
            experienceRequired: 1);
        var partial = MatchFeatureExtractor.Extract(
            JobSkills,
            new Dictionary<int, int> { [1] = 1, [3] = 1 },
            experienceRequired: 1);

        Assert.True(complete!.HasAllRequired);
        Assert.False(partial!.HasAllRequired);
        Assert.Equal(1, complete.MatchedWeightRatio, 10);
    }

    [Fact]
    public void Extract_JobWithoutSkills_HasNoFeatures()
    {
        var features = MatchFeatureExtractor.Extract(
            new Dictionary<int, double>(),
            new Dictionary<int, int> { [1] = 5 },
            experienceRequired: 2);

        Assert.Null(features);
    }
}
