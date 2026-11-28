using System.Reflection;
using System.Text.RegularExpressions;
using hotjob.DTOs.Candidate;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace hotjob.Tests;

/// <summary>Only the explicit Save button may write; EditForm submit never writes.</summary>
public sealed class CandidateCvReviewSubmitContractTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 test cv bytes"u8.ToArray();

    [Fact]
    public void Source_EditFormHasNoSubmitCallbacks_AndSaveButtonIsExplicit()
    {
        var source = ReadSource("hotjob/Components/Account/CandidateCvReview.razor");
        var formTags = Regex.Matches(source, "<EditForm\\b[^>]*>", RegexOptions.Singleline);
        var form = Assert.Single(formTags.Cast<Match>()).Value;
        Assert.Contains("EditContext=\"editContext\"", form);
        Assert.DoesNotContain("OnValidSubmit", form);
        Assert.DoesNotContain("OnInvalidSubmit", form);
        Assert.DoesNotContain("OnValidSubmit", source);
        Assert.DoesNotContain("OnInvalidSubmit", source);
        Assert.Matches("<button\\b[^>]*type=\"button\"[^>]*@onclick=\"SaveAsync\"", source);
    }

    [Fact]
    public async Task EditFormSubmit_HasNoDelegateAndCannotPersist_ExplicitSavePersistsOnce()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Explicit save only";

        var form = Assert.Single(page.Captured<EditForm>());
        var valid = typeof(EditForm)
            .GetProperty("OnValidSubmit", BindingFlags.Instance | BindingFlags.Public)!.GetValue(form);
        var invalid = typeof(EditForm)
            .GetProperty("OnInvalidSubmit", BindingFlags.Instance | BindingFlags.Public)!.GetValue(form);
        Assert.False(HasDelegate(valid));
        Assert.False(HasDelegate(invalid));

        var handle = typeof(EditForm).GetMethod("HandleSubmitAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(handle);
        await page.Dispatcher.InvokeAsync(() => EventCallback.Factory.Create(form, async () =>
        {
            if (handle.Invoke(form, null) is Task task) await task;
        }).InvokeAsync());

        Assert.Equal(before, page.ResumeCount);

        await page.EventAsync("SaveAsync");
        Assert.Equal(before + 1, page.ResumeCount);
    }

    private static bool HasDelegate(object? callback) => (bool)callback!.GetType()
        .GetProperty("HasDelegate", BindingFlags.Instance | BindingFlags.Public)!.GetValue(callback)!;

    private static string ReadSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "hotjob.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
