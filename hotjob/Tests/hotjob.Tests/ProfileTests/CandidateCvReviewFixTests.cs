using System.Reflection;
using hotjob.Components.Account;
using hotjob.DTOs.Candidate;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests;

/// <summary>Regressions from parent review round 1. Written before the fixes.</summary>
public sealed class CandidateCvReviewFixTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 test cv bytes"u8.ToArray();

    private static bool ShowCvReview(ReviewPage page) => (bool)typeof(hotjob.Components.Pages.Profile)
        .GetField("showCvReview", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(page.Profile)!;

    [Fact]
    public async Task PartialSuccess_InRealProfileKeepsChildDraftAndTypedFailure()
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { RenderProfile = true });
        var before = page.ResumeCount;
        await page.ProfileEventAsync("StartCvReview");
        Assert.NotNull(page.Child);
        var child = page.Child;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Partial profile";
        page.Storage.OnSave = (_, _, _) => Task.FromResult(ServiceResult<string>.Fail(
            ServiceErrorType.Validation, "This file could not be stored."));
        await page.EventAsync("SaveAsync");

        Assert.True(ShowCvReview(page));
        Assert.Same(child, page.Child);
        Assert.Equal("Partial profile", page.Draft?.Title);
        var html = await page.HtmlAsync();
        Assert.Contains("Resume saved, but the original file was not attached.", html);
        Assert.Contains("Review parsed CV", html);
        Assert.Equal(before + 1, page.ResumeCount);
        await page.EventAsync("SaveAsync");
        Assert.Equal(before + 1, page.ResumeCount);
    }

    [Fact]
    public async Task FullSuccess_InRealProfileClosesChildOnlyAfterRefresh()
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { RenderProfile = true });
        var before = page.ResumeCount;
        await page.ProfileEventAsync("StartCvReview");
        Assert.True(ShowCvReview(page));
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Full success";
        await page.EventAsync("SaveAsync");

        Assert.False(ShowCvReview(page));
        var html = await page.HtmlAsync();
        Assert.DoesNotContain("Review parsed CV", html);
        Assert.Contains("Resume saved", html);
        Assert.Equal(before + 1, page.ResumeCount);
    }

    [Fact]
    public async Task RefreshFailureAfterPartial_PreservesBothMessagesDraftAndLatch()
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            SavedCallbackThrows = true
        });
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Both failures";
        page.Storage.OnSave = (_, _, _) => Task.FromResult(ServiceResult<string>.Fail(
            ServiceErrorType.Validation, "This file could not be stored."));
        await page.EventAsync("SaveAsync");

        Assert.Equal("Both failures", page.Draft?.Title);
        var html = await page.HtmlAsync();
        Assert.Contains("Resume saved, but the original file was not attached.", html);
        Assert.Contains("Resume saved, but the CV list could not be refreshed.", html);
        Assert.Equal(before + 1, page.ResumeCount);
        await page.EventAsync("SaveAsync");
        Assert.Equal(before + 1, page.ResumeCount);
    }

    [Fact]
    public async Task CompletedChild_LocksParseManualSourceAndSave()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Locked review";
        page.Storage.OnSave = (_, _, _) => Task.FromResult(ServiceResult<string>.Fail(
            ServiceErrorType.Validation, "This file could not be stored."));
        await page.EventAsync("SaveAsync");
        Assert.Equal(before + 1, page.ResumeCount);
        var calls = page.Parser.Calls.Count;

        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        await page.EventAsync("ConfirmReplaceAsync");
        await page.EventAsync("StartManual");
        await page.SelectFileAsync("another.pdf", PdfBytes);
        await page.EventAsync("AddEducation");
        await page.EventAsync("AddWork");
        await page.EventAsync("SaveAsync");

        Assert.Equal(calls, page.Parser.Calls.Count);
        Assert.Equal(0, page.File!.OpenCount);
        Assert.Equal(before + 1, page.ResumeCount);
        Assert.Equal("Locked review", page.Draft?.Title);
        var html = await page.HtmlAsync();
        Assert.Contains("This review is locked to prevent duplicate creation", html);
        Assert.Contains("manage attachments from the resume card", html);
        Assert.Matches("<button[^>]*disabled[^>]*>Parse CV</button>", html);
    }

    [Fact]
    public async Task VisibleStatus_AndHonestAssistanceCopy()
    {
        var release = new TaskCompletionSource<ServiceResult<ParsedResumeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            AiAvailable = true, ParseHandler = _ => release.Task
        });
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        Task pending = Task.CompletedTask;
        try
        {
            await page.Dispatcher.InvokeAsync(() => { pending = page.EventAsync("ParseAsync", ResumeParseMode.Rules); });
            Assert.Contains("Parsing CV...", await page.HtmlAsync());
            Assert.Contains(true, page.BusyStates);
        }
        finally
        {
            release.SetResult(ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed()));
            await pending;
        }

        var html = await page.HtmlAsync();
        Assert.DoesNotContain(">Review a CV<", html);
        Assert.Contains("may be incomplete", html);
        Assert.Contains("Check every field", html);
        Assert.Contains("describes the original suggestion", html);
        Assert.Contains("manual entry", html);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task WorkValidation_IgnoresStaleEndOnlyForCurrent(bool isCurrent, bool expectCreate)
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Work dates";
        var work = page.Draft.WorkExperiences[0];
        work.IsCurrent = isCurrent;
        work.StartDate = new DateOnly(2024, 1, 1);
        work.EndDate = new DateOnly(2020, 1, 1);
        await page.EventAsync("SaveAsync");

        Assert.Equal(before + (expectCreate ? 1 : 0), page.ResumeCount);
    }

    [Fact]
    public async Task RealInputParsing_ErrorsBlockSaveThenRemovingRowClearsThem()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Input parsing";

        await page.ChangeInputAsync<InputDate<DateOnly?>>("not-a-date", 0);
        await page.ChangeInputAsync<InputNumber<double?>>("not-a-number", 0);
        await page.ChangeInputAsync<InputNumber<int>>("not-a-number", 0);
        await page.EventAsync("SaveAsync");
        Assert.Equal(before, page.ResumeCount);
        var blockedHtml = await page.HtmlAsync();
        Assert.Contains("must be", blockedHtml);

        var educationKey = page.Draft.Educations[0].Key;
        await page.EventAsync("RemoveEducation", educationKey);
        await page.EventAsync("RemoveSkill", page.Draft.Skills[0].Key);
        page.SetField("skillPickerId", 1);
        page.SetField("skillPickerYears", 2);
        await page.EventAsync("AddSkill");
        await page.EventAsync("SaveAsync");
        Assert.Equal(before + 1, page.ResumeCount);
    }

    [Theory]
    [InlineData(false, ResumeParseMode.Rules)]
    [InlineData(true, ResumeParseMode.Ai)]
    public async Task ButtonHandlers_UseOnlyTheClickedExplicitMode(bool capability, ResumeParseMode expected)
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { AiAvailable = capability });
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync(capability ? "OnParseAiClickAsync" : "OnParseRulesClickAsync");
        Assert.Equal([expected], page.Parser.Calls.Select(call => call.Mode));

        if (!capability)
        {
            await page.EventAsync("OnParseAiClickAsync");
            Assert.Single(page.Parser.Calls);
            Assert.Contains("AI parsing is not available", await page.HtmlAsync());
        }
    }

    [Fact]
    public async Task ManualOnly_SaveUsesNoParserFileOrStorage()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.EventAsync("StartManual");
        page.Draft!.Title = "Manual CV";
        await page.EventAsync("AddEducation");
        page.Draft.Educations[0].SchoolName = "Manual School";
        await page.EventAsync("AddWork");
        page.Draft.WorkExperiences[0].CompanyName = "Manual Company";
        page.SetField("skillPickerId", 2);
        page.SetField("skillPickerYears", 4);
        await page.EventAsync("AddSkill");
        await page.EventAsync("AddEducation");
        page.Draft.Educations[1].SchoolName = "Added School";
        await page.EventAsync("AddWork");
        page.Draft.WorkExperiences[1].CompanyName = "Added Company";
        Assert.Contains("manual entry", await page.HtmlAsync());
        await page.EventAsync("SaveAsync");

        Assert.Empty(page.Parser.Calls);
        Assert.Null(page.File);
        Assert.Equal(0, page.Storage.SaveCalls);
        Assert.Equal(before + 1, page.ResumeCount);
        var stored = (await page.StoredAsync(page.Fixture.Db.Resumes.Max(resume => resume.Id)))!;
        Assert.Equal(2, stored.Educations.Count);
        Assert.Equal(2, stored.WorkExperiences.Count);
    }

    [Fact]
    public async Task StreamReopenException_AfterCreateKeepsRowAndPartialOutcome()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes, throwOnOpenNumber: 2);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Stream fails";
        await page.EventAsync("SaveAsync");

        Assert.Equal(before + 1, page.ResumeCount);
        Assert.Equal(0, page.Storage.DeleteCalls);
        Assert.Equal("Stream fails", page.Draft?.Title);
        Assert.True(page.SavedResults[0].AttachmentFailed);
        Assert.Contains("Resume saved, but the original file was not attached.", await page.HtmlAsync());
        await page.EventAsync("SaveAsync");
        Assert.Equal(before + 1, page.ResumeCount);
    }

    [Fact]
    public async Task SelectParseReviewClose_LeavesRealSqliteUnchanged()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Unsaved";
        await page.EventAsync("AddEducation");
        await page.EventAsync("CancelAsync");
        Assert.Equal(before, page.ResumeCount);
        Assert.DoesNotContain(page.Parser.Calls, call => call.Mode == ResumeParseMode.Ai);
    }

    [Fact]
    public async Task ProfileBusy_ParentSaveDisabledAndEditClosesReview()
    {
        var release = new TaskCompletionSource<ServiceResult<ParsedResumeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            RenderProfile = true, ParseHandler = _ => release.Task
        });
        await page.ProfileEventAsync("StartCvReview");
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        Task pending = Task.CompletedTask;
        try
        {
            await page.Dispatcher.InvokeAsync(() => { pending = page.EventAsync("ParseAsync", ResumeParseMode.Rules); });
            var html = await page.HtmlAsync();
            Assert.Matches("<button[^>]*disabled[^>]*>Save changes</button>", html);
            var form = typeof(hotjob.Components.Pages.Profile)
                .GetField("profileForm", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(page.Profile);
            form!.GetType().GetProperty("FullName")!.SetValue(form, "Should not persist");
            await page.ProfileEventAsync("SaveProfileAsync");
            var candidate = await page.Fixture.Db.Candidates.SingleAsync(item => item.UserId == page.UserId);
            Assert.Equal("Match Candidate", candidate.FullName);
        }
        finally
        {
            release.SetResult(ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed()));
            await pending;
        }

        var resumeId = page.Fixture.Db.Resumes.Max(resume => resume.Id);
        await page.ProfileEventAsync("EditResumeAsync", resumeId);
        Assert.False(ShowCvReview(page));
        var finalHtml = await page.HtmlAsync();
        Assert.DoesNotContain("Review parsed CV", finalHtml);
        Assert.Contains("Edit resume", finalHtml);
    }

    [Fact]
    public void CurrentWork_StaleReversedEndIsIgnored()
    {
        var draft = ResumeReviewDraft.FromParsed(ReviewSamples.Parsed());
        var current = draft.WorkExperiences[0];
        current.IsCurrent = true;
        current.StartDate = new DateOnly(2024, 1, 1);
        current.EndDate = new DateOnly(2020, 1, 1);
        Assert.DoesNotContain("Work experience entry 1: end date cannot be before the start date.",
            draft.Validate([new(1, "C#", null)]));

        var ordinary = draft.WorkExperiences[0];
        ordinary.IsCurrent = false;
        Assert.Contains("Work experience entry 1: end date cannot be before the start date.",
            draft.Validate([new(1, "C#", null)]));
    }
}
