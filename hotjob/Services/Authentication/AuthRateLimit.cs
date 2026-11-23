namespace hotjob.Services.Authentication;

/// <summary>Shared settings for the rate-limit policy applied to login and registration.</summary>
public static class AuthRateLimit
{
    public const string PolicyName = "auth";
    public const int PermitLimit = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
