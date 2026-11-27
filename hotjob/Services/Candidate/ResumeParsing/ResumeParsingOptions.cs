namespace hotjob.Services.Candidate.ResumeParsing;

public sealed class ResumeParsingOptions
{
    public bool UseAi { get; set; }
    public int AiDailyLimit { get; set; } = 10;
    public int AiPerUserDailyLimit { get; set; } = 3;
}
