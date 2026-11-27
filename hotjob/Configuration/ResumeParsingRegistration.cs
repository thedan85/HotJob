using System.Globalization;
using hotjob.Services.Candidate;
using hotjob.Services.Candidate.ResumeParsing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace hotjob.Configuration;

public static class ResumeParsingRegistration
{
    public static IServiceCollection AddResumeParsing(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<ResumeParsingOptions>(o =>
        {
            o.UseAi = bool.TryParse(configuration["ResumeParsing:UseAi"], out var value) && value;
            o.AiDailyLimit = Integer("ResumeParsing:AiDailyLimit", 10);
            o.AiPerUserDailyLimit = Integer("ResumeParsing:AiPerUserDailyLimit", 3);
        });
        int Integer(string key, int fallback) => configuration[key] is not { } value ? fallback
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? Math.Max(0, number) : 0;
        services.Configure<DeepSeekOptions>(o =>
        {
            o.ApiKey = configuration["DeepSeek:ApiKey"] ?? "";
            o.BaseUrl = configuration["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";
            o.Model = configuration["DeepSeek:Model"] ?? "deepseek-chat";
        });
        services.AddSingleton<ResumeExtractionRunner>();
        services.AddSingleton<ResumeAiUsageLimiter>();
        services.AddSingleton<PdfResumeTextExtractor>();
        services.AddSingleton<DocxResumeTextExtractor>();
        services.AddSingleton<ResumeTextExtractor>();
        services.AddSingleton<RuleBasedResumeParser>();
        services.AddSingleton<ParsedResumeNormalizer>();
        services.AddSingleton<ResumeContactRedactor>();
        services.AddScoped<DeepSeekResumeClient>();
        services.AddScoped<IResumeParserService, ResumeParserService>();
        services.AddHttpClient(DeepSeekResumeClient.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers()
            .RedactLoggedHeaders(["Authorization"]);
        return services;
    }
}
