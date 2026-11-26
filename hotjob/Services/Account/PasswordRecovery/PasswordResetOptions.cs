namespace hotjob.Services.Account.PasswordRecovery;

public sealed class PasswordResetOptions
{
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);
}
