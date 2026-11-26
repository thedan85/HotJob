using hotjob.Configuration;
using hotjob.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace hotjob.Tests.PasswordRecoveryTests;

public sealed class EmailTests
{
    [Fact]
    public async Task SmtpFailure_RequiresStartTlsAndNeverLogsExceptionPayload()
    {
        var log = new RecordingLogger<SmtpEmailSender>();
        var client = new FailingSmtpClient();
        var options = Options.Create(new EmailOptions
        {
            Host = "smtp.example.test", Username = "dummy-user", Password = "SENTINEL_PASSWORD",
            From = "sender@example.test"
        });
        await new SmtpEmailSender(options, log, () => client)
            .SendAsync("recipient@example.test", "subject", "SENTINEL_TOKEN");
        Assert.Equal(SecureSocketOptions.StartTls, client.RequestedSecurity);
        Assert.Equal(587, client.RequestedPort);
        Assert.Equal(15_000, client.Timeout);
        Assert.True(client.Disposed);
        Assert.Contains(nameof(InvalidOperationException), log.Text);
        Assert.DoesNotContain("SENTINEL", log.Text);
        Assert.All(log.Exceptions, exception => Assert.Null(exception));
    }

    [Theory]
    [InlineData("Development", true, true, true, "SmtpEmailSender")]
    [InlineData("Production", true, true, true, "SmtpEmailSender")]
    [InlineData("Staging", true, true, true, "SmtpEmailSender")]
    [InlineData("Development", false, true, true, "DevelopmentEmailSender")]
    [InlineData("Development", true, false, true, "DevelopmentEmailSender")]
    [InlineData("Development", true, true, false, "DevelopmentEmailSender")]
    [InlineData("Production", false, true, true, "NotConfiguredEmailSender")]
    [InlineData("Staging", true, false, true, "NotConfiguredEmailSender")]
    [InlineData("Production", true, true, false, "NotConfiguredEmailSender")]
    public void Selection_UsesConfigurationAndEnvironment(
        string environment, bool host, bool username, bool password, string expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Host"] = host ? "smtp.example.test" : " ",
            ["Email:Username"] = username ? "dummy-user" : "",
            ["Email:Password"] = password ? "dummy-password" : null
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRecoveryEmail(config, new TestEnvironment(environment));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(expected, provider.GetRequiredService<IEmailSender>().GetType().Name);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public async Task DevelopmentSender_OnlyLogsContentsInDevelopment(string environment, bool logsBody)
    {
        var log = new RecordingLogger<DevelopmentEmailSender>();
        var sender = new DevelopmentEmailSender(new TestEnvironment(environment), log);
        await sender.SendAsync("recipient@example.test", "subject", "SENTINEL_RESET_LINK");
        Assert.Equal(logsBody, log.Text.Contains("SENTINEL_RESET_LINK"));
        if (!logsBody) Assert.DoesNotContain("recipient@example.test", log.Text);
    }

    [Fact]
    public async Task NotConfiguredSender_LogsOnlyFixedWarning()
    {
        var log = new RecordingLogger<NotConfiguredEmailSender>();
        await new NotConfiguredEmailSender(log).SendAsync(
            "SENTINEL_RECIPIENT", "SENTINEL_SUBJECT", "SENTINEL_TOKEN");
        Assert.NotEmpty(log.Text);
        Assert.DoesNotContain("SENTINEL", log.Text);
        Assert.All(log.Exceptions, exception => Assert.Null(exception));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void EnvironmentFile_LoadsOnlyDevelopmentAndTranslatesKeys(string environment, bool loads)
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            File.WriteAllText(Path.Combine(path, ".env.local"),
                "# dummy only\nexport Email__Host='smtp.example.test'\nEmail__Password=\"dummy=a=b\"\n");
            var config = new ConfigurationManager();
            DevelopmentEnvironmentFile.Load(config, new TestEnvironment(environment) { ContentRootPath = path }, path);
            Assert.Equal(loads ? "smtp.example.test" : null, config["Email:Host"]);
            Assert.Equal(loads ? "dummy=a=b" : null, config["Email:Password"]);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void EnvironmentFile_MissingFileIsSafe()
    {
        var config = new ConfigurationManager();
        var missing = Path.Combine(Path.GetTempPath(), "opencode", Guid.NewGuid().ToString("N"));
        DevelopmentEnvironmentFile.Load(config, new TestEnvironment("Development") { ContentRootPath = missing }, missing);
        Assert.Null(config["Email:Host"]);
    }
}

internal sealed class FailingSmtpClient : SmtpClient
{
    public SecureSocketOptions RequestedSecurity { get; private set; }
    public int RequestedPort { get; private set; }
    public bool Disposed { get; private set; }
    public override Task ConnectAsync(string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto,
        CancellationToken cancellationToken = default)
    {
        RequestedSecurity = options;
        RequestedPort = port;
        throw new InvalidOperationException("SENTINEL_PASSWORD SENTINEL_TOKEN");
    }
    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class TestEnvironment(string environment = "Development") : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environment;
    public string ApplicationName { get; set; } = "RecoveryTests";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public List<Exception?> Exceptions { get; } = [];
    public string Text => string.Join("\n", Messages);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        Exceptions.Add(exception);
    }
}
