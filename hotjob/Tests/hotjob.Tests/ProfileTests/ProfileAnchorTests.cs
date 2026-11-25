using System.Text.RegularExpressions;

namespace hotjob.Tests;

public sealed class ProfileAnchorTests
{
    [Theory]
    [InlineData("personal-info")]
    [InlineData("cv-management")]
    [InlineData("preferences")]
    [InlineData("change-password")]
    [InlineData("avatar-url")]
    public void ProfileShortcut_ResolvesToProfileAndHasMatchingTarget(string targetId)
    {
        // Guard the actual Razor markup without extracting/refactoring the profile page for testing.
        // Browser checks cover activation and scrolling; this test requires the source checkout.
        var profile = ReadSource("hotjob/Components/Pages/Profile.razor");
        var passwordSection = ReadSource("hotjob/Components/Account/ChangePasswordSection.razor");
        var app = ReadSource("hotjob/Components/App.razor");
        var baseMatch = Regex.Match(app, "<base\\s+href=\"(?<href>[^\"]+)\"");
        Assert.True(baseMatch.Success);
        var documentBase = new Uri(new Uri("https://hotjob.test/profile"), baseMatch.Groups["href"].Value);
        var links = Regex.Matches(profile, $"<a\\b[^>]*\\bhref=\"(?<href>[^\"]*#{Regex.Escape(targetId)})\"");
        var link = Assert.Single(links.Cast<Match>());

        var destination = new Uri(documentBase, link.Groups["href"].Value);

        Assert.Equal("/profile", destination.AbsolutePath);
        Assert.Equal($"#{targetId}", destination.Fragment);
        Assert.Contains($"id=\"{targetId}\"", profile + passwordSection);
    }

    private static string ReadSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "hotjob.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
