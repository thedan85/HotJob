using Microsoft.Extensions.Options;

namespace hotjob.Services.Email;

public static class EmailRegistration
{
    public static IServiceCollection AddRecoveryEmail(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.AddTransient<IEmailSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.Host) &&
                !string.IsNullOrWhiteSpace(options.Username) &&
                !string.IsNullOrWhiteSpace(options.Password))
                return ActivatorUtilities.CreateInstance<SmtpEmailSender>(provider);
            if (environment.IsDevelopment())
                return new DevelopmentEmailSender(environment,
                    provider.GetRequiredService<ILogger<DevelopmentEmailSender>>());
            return new NotConfiguredEmailSender(provider.GetRequiredService<ILogger<NotConfiguredEmailSender>>());
        });
        return services;
    }
}
