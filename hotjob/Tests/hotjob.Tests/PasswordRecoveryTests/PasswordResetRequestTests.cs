using System.Net;
using hotjob.Models.Users;
using hotjob.Services.Account.PasswordRecovery;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;

namespace hotjob.Tests.PasswordRecoveryTests;

public sealed class PasswordResetRequestTests
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("disabled")]
    [InlineData("google")]
    [InlineData("active")]
    [InlineData("failure")]
    public async Task Request_IsGenericAndOnlyActiveLocalAccountsReceiveTokens(string kind)
    {
        var sender = new FakeEmailSender();
        var log = new RecordingLogger<PasswordResetDelivery>();
        var probe = new TokenGenerationProbe();
        await using var fx = await RecoveryFixture.CreateAsync(sender, services =>
        {
            services.AddSingleton<ILogger<PasswordResetDelivery>>(log);
            services.AddSingleton(probe);
            services.AddTransient<TrackingResetTokenProvider>();
            services.Configure<IdentityOptions>(options =>
            {
                options.Tokens.ProviderMap["TrackedReset"] = new TokenProviderDescriptor(typeof(TrackingResetTokenProvider));
                options.Tokens.PasswordResetTokenProvider = "TrackedReset";
            });
        });
        var email = "a+b@example.test";
        ApplicationUser? user = null;
        if (kind is "active" or "disabled" or "failure")
        {
            user = await fx.SeedUserAsync(email);
            if (kind == "disabled")
            {
                user.IsActive = false;
                Assert.True((await fx.UserManager.UpdateAsync(user)).Succeeded);
            }
        }
        if (kind == "google")
        {
            user = new ApplicationUser { UserName = email, Email = email, IsActive = true };
            Assert.True((await fx.UserManager.CreateAsync(user)).Succeeded);
            Assert.True((await fx.UserManager.AddLoginAsync(user, new UserLoginInfo("Google", "test-key", "Google"))).Succeeded);
        }
        if (kind == "failure") sender.Failure = new InvalidOperationException("SENTINEL_PASSWORD SENTINEL_TOKEN");

        var result = await fx.GetService<IPasswordResetService>().RequestPasswordResetAsync(email, "https://example.test/");
        Assert.True(result.Success);
        Assert.Equal(PasswordResetService.RequestNotice, result.Data);
        Assert.Empty(sender.Messages); // Account lookup and email happen only in the worker.
        Assert.Equal(0, probe.Generated);
        var queue = fx.GetService<PasswordResetQueue>();
        queue.Complete();
        await foreach (var job in queue.ReadAllAsync(CancellationToken.None))
            await fx.GetService<PasswordResetDelivery>().DeliverAsync(job, CancellationToken.None);

        if (kind == "active")
        {
            var message = Assert.Single(sender.Messages);
            Assert.Equal(email, message.To);
            Assert.Contains("1 hour", message.Body);
            Assert.Contains("&amp;token=", message.Body);
            var link = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(message.Body, "href=\"([^\"]+)\"").Groups[1].Value);
            var query = QueryHelpers.ParseQuery(new Uri(link).Query);
            Assert.Equal(email, query["email"]);
            var token = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["token"].ToString()));
            Assert.True(await fx.UserManager.VerifyUserTokenAsync(user!,
                fx.UserManager.Options.Tokens.PasswordResetTokenProvider, UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token));
        }
        else Assert.Empty(sender.Messages);
        Assert.Equal(kind is "active" or "failure" ? 1 : 0, probe.Generated);
        Assert.DoesNotContain("SENTINEL", log.Text);
        Assert.All(log.Exceptions, exception => Assert.Null(exception));
    }

    [Fact]
    public async Task Request_DoesNotWaitForSlowEmailAndWorkerUsesIndependentScopes()
    {
        var sender = new FakeEmailSender { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var workerManagers = new List<UserManager<ApplicationUser>>();
        await using var fx = await RecoveryFixture.CreateAsync(sender, services =>
            services.AddScoped<hotjob.Services.Email.IEmailSender>(provider =>
            {
                workerManagers.Add(provider.GetRequiredService<UserManager<ApplicationUser>>());
                return sender;
            }));
        await fx.SeedUserAsync("one@example.test");
        await fx.SeedUserAsync("two@example.test");
        var worker = fx.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<PasswordResetWorker>().Single();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var service = fx.GetService<IPasswordResetService>();
            Assert.True((await service.RequestPasswordResetAsync("one@example.test", "https://example.test/")).Success);
            await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var next = service.RequestPasswordResetAsync("two@example.test", "https://example.test/");
            Assert.True(next.IsCompletedSuccessfully);
            Assert.True((await next).Success);
            // The worker must not dispose the caller's UserManager/DbContext.
            Assert.NotNull(await fx.UserManager.FindByEmailAsync("one@example.test"));
        }
        finally
        {
            sender.Release.SetResult();
            fx.GetService<PasswordResetQueue>().Complete();
            // Wait for the worker to drain before stopping it.
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
            await worker.StopAsync(CancellationToken.None);
        }
        Assert.Equal(2, sender.Messages.Count);
        Assert.Equal(2, workerManagers.Count);
        Assert.NotSame(workerManagers[0], workerManagers[1]);
        Assert.All(workerManagers, manager => Assert.NotSame(fx.UserManager, manager));
    }

    [Fact]
    public async Task Request_QueueFullLimitedAndMalformedRemainGeneric()
    {
        var sender = new FakeEmailSender();
        await using var fx = await RecoveryFixture.CreateAsync(sender);
        var service = fx.GetService<IPasswordResetService>();
        var queue = fx.GetService<PasswordResetQueue>();
        for (var i = 0; i < 100; i++) queue.TryEnqueue(new("full@example.test", "https://example.test/"));
        foreach (var email in new[] { "valid@example.test", "valid@example.test", "", "not-an-email", new string('x', 255) })
        {
            var result = await service.RequestPasswordResetAsync(email, "https://example.test/");
            Assert.True(result.Success);
            Assert.Equal(PasswordResetService.RequestNotice, result.Data);
        }
    }

    [Fact]
    public async Task ConfiguredLifetime_IsAppliedToIdentityAndEmail()
    {
        var sender = new FakeEmailSender();
        await using var fx = await RecoveryFixture.CreateAsync(sender, settings: new()
        {
            ["PasswordReset:TokenLifetime"] = "00:30:00",
            ["App:BaseUrl"] = "https://public.example/app/"
        });
        await fx.SeedUserAsync();
        Assert.Equal(TimeSpan.FromMinutes(30), fx.GetService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        await fx.GetService<IPasswordResetService>().RequestPasswordResetAsync("user@example.com", "https://fallback.test/");
        var queue = fx.GetService<PasswordResetQueue>();
        queue.Complete();
        await foreach (var job in queue.ReadAllAsync(CancellationToken.None))
            await fx.GetService<PasswordResetDelivery>().DeliverAsync(job, CancellationToken.None);
        var message = Assert.Single(sender.Messages);
        Assert.Contains("30 minutes", message.Body);
        Assert.Contains("https://public.example/app/reset-password?", message.Body);
        Assert.DoesNotContain("fallback.test", message.Body);
    }

    [Fact]
    public async Task Worker_ContinuesAfterDeliveryFailureWithoutUnsafeLogs()
    {
        var sender = new FakeEmailSender { Failure = new InvalidOperationException("SENTINEL_TOKEN") };
        var logs = new RecordingLogger<PasswordResetDelivery>();
        await using var fx = await RecoveryFixture.CreateAsync(sender, services =>
            services.AddSingleton<ILogger<PasswordResetDelivery>>(logs));
        await fx.SeedUserAsync("first@example.test");
        await fx.SeedUserAsync("second@example.test");
        var service = fx.GetService<IPasswordResetService>();
        await service.RequestPasswordResetAsync("first@example.test", "https://example.test/");
        await service.RequestPasswordResetAsync("second@example.test", "https://example.test/");
        var queue = fx.GetService<PasswordResetQueue>();
        queue.Complete();
        var worker = fx.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<PasswordResetWorker>().Single();
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(2, logs.Messages.Count);
        Assert.DoesNotContain("SENTINEL", logs.Text);
        Assert.All(logs.Exceptions, exception => Assert.Null(exception));
    }
}

internal sealed class TokenGenerationProbe
{
    public int Generated { get; set; }
}

internal sealed class TrackingResetTokenProvider(
    IDataProtectionProvider protection,
    IOptions<DataProtectionTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger,
    TokenGenerationProbe probe) : DataProtectorTokenProvider<ApplicationUser>(protection, options, logger)
{
    public override Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        probe.Generated++;
        return base.GenerateAsync(purpose, manager, user);
    }
}
