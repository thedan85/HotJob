namespace hotjob.Services.Candidate.ResumeParsing;

public sealed class DeepSeekOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-chat";
}
