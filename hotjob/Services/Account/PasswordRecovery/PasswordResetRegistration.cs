using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace hotjob.Services.Account.PasswordRecovery;

public static class PasswordResetRegistration
{
    public static IServiceCollection AddPasswordRecovery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PasswordResetOptions>()
            .Bind(configuration.GetSection("PasswordReset"))
            .Validate(options => options.TokenLifetime > TimeSpan.Zero, "Password reset token lifetime must be positive.")
            .ValidateOnStart();
        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<PasswordResetOptions>>((provider, recovery) =>
                provider.TokenLifespan = recovery.Value.TokenLifetime);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<PasswordResetCallerContext>();
        services.AddScoped<CircuitHandler, PasswordResetCircuitHandler>();
        services.AddSingleton<PasswordResetRateLimiter>();
        services.AddSingleton<PasswordResetQueue>();
        services.AddSingleton<PasswordResetLink>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<PasswordResetDelivery>();
        services.AddHostedService<PasswordResetWorker>();
        return services;
    }
}
