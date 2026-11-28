using System.Reflection;
using hotjob.Components.Account;
using hotjob.DTOs.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;

namespace hotjob.Tests;

/// <summary>Scoped round-2 regressions: parent busy release and ignored current end date.</summary>
public sealed class CandidateCvReviewBusyTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 test cv bytes"u8.ToArray();

    private static bool ProfileBool(ReviewPage page, string name) => (bool)typeof(hotjob.Components.Pages.Profile)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page.Profile)!;

    private static async Task AssertParentUsable(ReviewPage page)
    {
        Assert.False(ProfileBool(page, "cvReviewBusy"));
        Assert.False(ProfileBool(page, "showCvReview"));
        var html = await page.HtmlAsync();
        Assert.Contains(">Save changes</button>", html);
        Assert.DoesNotMatch("<button[^>]*disabled[^>]*>Save changes</button>", html);
        Assert.DoesNotMatch("<button[^>]*disabled[^>]*>Add CV</button>", html);
    }

    [Fact]
    public async Task FullAttachmentSuccess_ReleasesParentBusyAndReenablesProfileActions()
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { RenderProfile = true });
        await page.ProfileEventAsync("StartCvReview");
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Attached CV";
        await page.EventAsync("SaveAsync");

        await AssertParentUsable(page);
        await page.ProfileEventAsync("OpenCreateChoice");
        Assert.Contains("Create resume from a CV file", await page.HtmlAsync());
    }

    [Fact]
    public async Task ManualNoFileSuccess_ReleasesParentBusyAndReenablesProfileActions()
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { RenderProfile = true });
        await page.ProfileEventAsync("StartCvReview");
        await page.EventAsync("StartManual");
        page.Draft!.Title = "Manual no file";
        page.SetField("skillPickerId", 1);
        page.SetField("skillPickerYears", 2);
        await page.EventAsync("AddSkill");
        await page.EventAsync("SaveAsync");

        Assert.Equal(0, page.Storage.SaveCalls);
        await AssertParentUsable(page);
        await page.ProfileEventAsync("OpenCreateChoice");
        Assert.Contains("Create resume from a CV file", await page.HtmlAsync());
    }

    [Fact]
    public async Task ParseCloseLateCompletion_ReleasesBusyCancelsTokenAndIgnoresLateResult()
    {
        ParsedCall? captured = null;
        var release = new TaskCompletionSource<ServiceResult<ParsedResumeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            RenderProfile = true,
            ParseHandler = call =>
            {
                captured = call;
                return release.Task;
            }
        });
        var before = page.ResumeCount;
        await page.ProfileEventAsync("StartCvReview");
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        Task pending = Task.CompletedTask;
        try
        {
            await page.Dispatcher.InvokeAsync(() =>
            {
                pending = page.EventAsync("ParseAsync", ResumeParseMode.Rules);
            });
            await page.Dispatcher.InvokeAsync(() => { });
            Assert.True(ProfileBool(page, "cvReviewBusy"));
            Assert.Matches("<button[^>]*disabled[^>]*>Save changes</button>", await page.HtmlAsync());

            await page.EventAsync("CancelAsync");
            Assert.NotNull(captured);
            Assert.True(captured!.Token.IsCancellationRequested);
            await AssertParentUsable(page);
        }
        finally
        {
            release.TrySetResult(ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed()));
            await pending;
        }

        Assert.False(ProfileBool(page, "cvReviewBusy"));
        Assert.Equal(before, page.ResumeCount);
        var html = await page.HtmlAsync();
        Assert.DoesNotContain("Resume saved", html);
        Assert.Null(page.Draft);
    }

    [Fact]
    public async Task ManualDraftCopy_MarksManualWithoutFabricatedConfidence()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.EventAsync("StartManual");
        await page.EventAsync("AddEducation");
        await page.EventAsync("AddWork");
        var manualHtml = await page.HtmlAsync();
        Assert.Contains(">Manual entry</p>", manualHtml);
        Assert.DoesNotContain("Mode used: Rules", manualHtml);
        Assert.Contains(">manual entry</span>", manualHtml);
        Assert.DoesNotContain("Confidence: Low (manual entry)", manualHtml);

        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var parsedHtml = await page.HtmlAsync();
        Assert.Contains("Mode used: Rules", parsedHtml);
        Assert.DoesNotContain(">Manual entry</p>", parsedHtml);
    }

    [Fact]
    public async Task CurrentJobInvalidEndDate_DoesNotLeaveStaleParsingError()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Current job";
        // Work row 1 EndDate is the sixth InputDate in this sample render.
        await page.ChangeInputAsync<InputDate<DateOnly?>>("not-a-date", 5);
        await page.EventAsync("SaveAsync");
        Assert.Equal(before, page.ResumeCount);

        page.Draft.WorkExperiences[0].IsCurrent = true;
        await page.EventAsync("CancelReplace"); // re-render: current job hides its End input
        Assert.Empty(page.ReviewEditContext!.GetValidationMessages());
        await page.EventAsync("SaveAsync");

        Assert.Equal(before + 1, page.ResumeCount);
        var stored = (await page.StoredAsync(page.Fixture.Db.Resumes.Max(resume => resume.Id)))!;
        Assert.Contains(stored.WorkExperiences, work => work.CompanyName == "First Company" && work.EndDate is null);
    }
}
