using hotjob.Configuration;
using hotjob.Services.Candidate;
using hotjob.Services.Candidate.ResumeParsing;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ResumeParsingRegistrationTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("true", "test-key", true)]
    [InlineData("bad-bool", "test-key", false)]
    [InlineData("true", "", false)]
    public async Task SafeConfigurationAndLifetimes(string? enabled, string? key, bool available)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ResumeParsing:UseAi"] = enabled, ["DeepSeek:ApiKey"] = key, ["ResumeParsing:AiDailyLimit"] = "not-an-integer" }).Build();
        await using var fx = await TestFixture.CreateAsync(services =>
        {
            services.AddHttpContextAccessor();
            services.AddScoped<AuthenticationStateProvider, TestFixture.TestAuthenticationStateProvider>();
            services.AddResumeParsing(config);
        });
        using var a = fx.Services.CreateScope();
        using var b = fx.Services.CreateScope();
        Assert.Equal(available, a.ServiceProvider.GetRequiredService<IResumeParserService>().IsAiAvailable);
        Assert.NotSame(a.ServiceProvider.GetRequiredService<IResumeParserService>(), b.ServiceProvider.GetRequiredService<IResumeParserService>());
        Assert.Same(a.ServiceProvider.GetRequiredService<ResumeAiUsageLimiter>(), b.ServiceProvider.GetRequiredService<ResumeAiUsageLimiter>());
        Assert.Same(a.ServiceProvider.GetRequiredService<ResumeExtractionRunner>(), b.ServiceProvider.GetRequiredService<ResumeExtractionRunner>());
        Assert.Equal(0, a.ServiceProvider.GetRequiredService<IOptions<ResumeParsingOptions>>().Value.AiDailyLimit);
        Assert.Equal("deepseek-chat", a.ServiceProvider.GetRequiredService<IOptions<DeepSeekOptions>>().Value.Model);
    }
}
