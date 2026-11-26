using hotjob.Services.Account.PasswordRecovery;
using hotjob.Services.Authentication;
using hotjob.Services.Email;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace hotjob.Tests.PasswordRecoveryTests;

internal static class RecoveryFixture
{
    public static Task<TestFixture> CreateAsync(FakeEmailSender sender, Action<IServiceCollection>? configure = null,
        Dictionary<string, string?>? settings = null) => TestFixture.CreateAsync(services =>
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IEmailSender>(sender);
        services.AddSingleton(TimeProvider.System);
        services.Configure<JwtOptions>(options =>
        {
            options.SecretKey = "test-secret-key-at-least-32-chars-long";
            options.Issuer = "HotJobTest";
            options.Audience = "HotJobTestClient";
            options.RefreshTokenDays = 7;
            options.AccessTokenMinutes = 60;
        });
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddPasswordRecovery(config);
        services.Replace(ServiceDescriptor.Singleton<IHttpContextAccessor>(
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    Connection = { RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1") }
                }
            }));
        configure?.Invoke(services);
    });
}

internal sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Messages { get; } = [];
    public Exception? Failure { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? Release { get; set; }
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        Entered.TrySetResult();
        if (Release is not null) await Release.Task.WaitAsync(ct);
        if (Failure is not null) throw Failure;
        Messages.Add((to, subject, htmlBody));
    }
}
