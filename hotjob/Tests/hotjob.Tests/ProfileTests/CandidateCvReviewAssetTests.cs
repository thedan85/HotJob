namespace hotjob.Tests;

/// <summary>Source-asset contract: this app loads Profile.css, not a CSS isolation bundle.</summary>
public sealed class CandidateCvReviewAssetTests
{
    [Fact]
    public void ReviewStyles_AreLoadedFromProfileCssAndRemainScoped()
    {
        var root = FindRoot();
        var app = File.ReadAllText(Path.Combine(root, "hotjob/Components/App.razor"));
        var css = File.ReadAllText(Path.Combine(root, "hotjob/wwwroot/css/Profile.css"));

        Assert.Contains("<link rel=\"stylesheet\" href=\"css/Profile.css\" />", app);
        Assert.DoesNotContain("hotjob.styles.css", app);
        Assert.False(File.Exists(Path.Combine(root,
            "hotjob/Components/Account/CandidateCvReview.razor.css")));

        Assert.Contains(".cv-review {", css);
        Assert.Contains("overflow-wrap: anywhere", css);
        Assert.Contains(".cv-review fieldset", css);
        Assert.Contains(".cv-review .cv-review-file input[type=\"file\"]", css);
        Assert.Contains(".cv-review .cv-form-row", css);
        Assert.Contains(".cv-review .cv-review-actions", css);
        Assert.Contains("@media (max-width: 760px)", css);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "hotjob.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
