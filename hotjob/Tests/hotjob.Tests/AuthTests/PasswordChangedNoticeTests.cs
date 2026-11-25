using hotjob.Components.Account;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace hotjob.Tests;

public sealed class PasswordChangedNoticeTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("INJECTED_QUERY_NOTICE", false)]
    public async Task Notice_OnlyFixedFlagShowsHardcodedNotice(string flag, bool showsNotice)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PasswordChangedNotice.Flag)] = flag
            });
            var component = await renderer.RenderComponentAsync<PasswordChangedNotice>(parameters);
            return component.ToHtmlString();
        });

        const string notice = "Password changed successfully. Please sign in with your new password.";
        Assert.Equal(showsNotice, html.Contains(notice, StringComparison.Ordinal));
        Assert.DoesNotContain("INJECTED_QUERY_NOTICE", html);
        if (showsNotice)
            Assert.Contains("role=\"status\"", html);
    }
}
