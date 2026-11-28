using hotjob.Components.Account;
using hotjob.DTOs.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;

namespace hotjob.Tests;

public sealed class CandidateCvReviewTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 test cv bytes"u8.ToArray();

    [Fact]
    public async Task AiVisibility_RequiresCapabilityAndDisclosure()
    {
        await using var hidden = await ReviewPage.CreateAsync(new ReviewOptions { AiAvailable = false });
        var hiddenHtml = await hidden.HtmlAsync();
        Assert.DoesNotContain("Parse with AI", hiddenHtml);
        Assert.DoesNotContain("external AI provider", hiddenHtml);

        await using var shown = await ReviewPage.CreateAsync(new ReviewOptions { AiAvailable = true });
        var html = await shown.HtmlAsync();
        Assert.Contains("Parse with AI", html);
        Assert.Contains("external AI provider", html);
        foreach (var word in new[] { "emails", "phone numbers", "links", "addresses" })
            Assert.Contains(word, html);
        Assert.Contains("Names, schools, companies, and dates may still be included", html);
    }

    [Fact]
    public async Task Parse_UsesOnlyExplicitModeAndDoesNotPersist()
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { AiAvailable = true });
        var before = page.ResumeCount;

        await page.SelectFileAsync("cv-rules.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);

        Assert.Equal([ResumeParseMode.Rules], page.Parser.Calls.Select(call => call.Mode));
        Assert.Equal(before, page.ResumeCount);
        Assert.Equal(page.UserId, Assert.Single(page.Parser.Calls).UserId);

        await page.SelectFileAsync("cv-ai.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Ai);

        Assert.Equal([ResumeParseMode.Rules, ResumeParseMode.Ai], page.Parser.Calls.Select(call => call.Mode));
        Assert.Equal(before, page.ResumeCount);
        Assert.Empty(page.SavedMessages);
    }

    [Fact]
    public async Task Busy_GuardsRepeatedAndCrossActionEvents()
    {
        var release = new TaskCompletionSource<ServiceResult<ParsedResumeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            AiAvailable = true, ParseHandler = _ => release.Task
        });
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);

        Task pending = Task.CompletedTask;
        await page.Dispatcher.InvokeAsync(() =>
        {
            pending = page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        });
        Assert.False(pending.IsCompleted);

        var html = await page.HtmlAsync();
        Assert.Contains("aria-busy=\"true\"", html);
        Assert.Matches("<button[^>]*disabled[^>]*>Parse CV</button>", html);
        Assert.Matches("<button[^>]*disabled[^>]*>Parse with AI</button>", html);
        Assert.Matches("<button[^>]*disabled[^>]*>Save CV</button>", html);

        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        await page.EventAsync("StartManual");
        await page.EventAsync("SaveAsync");
        Assert.Single(page.Parser.Calls);
        Assert.Equal(before, page.ResumeCount);

        release.SetResult(ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed()));
        await pending;
    }

    [Fact]
    public async Task Render_ShowsActualModeWarningsConfidenceAndEncodedHints()
    {
        var parsed = ReviewSamples.Parsed() with
        {
            ModeUsed = ResumeParseMode.Rules,
            Summary = "<b>Summary <script>alert(1)</script></b>",
            SummaryConfidence = ResumeParseConfidence.High,
            Warnings = ["<script>alert('warning')</script>"],
            UnrecognizedSkills = ["<svg>unknown</svg>"],
            Educations =
            [
                new(new() { SchoolName = "<img src=x onerror=alert(1)>", Major = "Degree" },
                    "Degree", "Field", ResumeParseConfidence.Medium)
            ],
            WorkExperiences =
            [
                new(new() { CompanyName = "Company" }, false, ResumeParseConfidence.Low)
            ]
        };
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            AiAvailable = true, ParseResult = ServiceResult<ParsedResumeDto>.Ok(parsed)
        });
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Ai);

        var html = await page.HtmlAsync();
        Assert.Contains("Mode used: Rules", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img", html);
        Assert.Contains("&lt;svg&gt;unknown&lt;/svg&gt;", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<svg", html);
        foreach (var confidence in new[] { "High", "Medium", "Low" })
            Assert.Contains(confidence, html);
    }

    [Fact]
    public async Task Save_InvalidTitleOrSkillsDoesNotPersist()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);

        page.Draft!.Title = "   ";
        await page.EventAsync("SaveAsync");
        Assert.Equal(before, page.ResumeCount);
        Assert.Contains("Resume title is required.", await page.HtmlAsync());

        page.Draft.Title = "Reviewed CV";
        foreach (var key in page.Draft.Skills.Select(skill => skill.Key).ToArray())
            await page.EventAsync("RemoveSkill", key);
        await page.EventAsync("SaveAsync");
        Assert.Equal(before, page.ResumeCount);
        Assert.Contains("Add at least one skill before saving.", await page.HtmlAsync());

        page.SetField("skillPickerId", 1);
        page.SetField("skillPickerYears", 3);
        await page.EventAsync("AddSkill");
        await page.Dispatcher.InvokeAsync(() =>
        {
            var store = new ValidationMessageStore(page.ReviewEditContext!);
            store.Add(new FieldIdentifier(page.Draft!, nameof(ResumeReviewDraft.Title)), "A date was not valid.");
            page.ReviewEditContext!.NotifyValidationStateChanged();
        });
        await page.EventAsync("SaveAsync");
        Assert.Equal(before, page.ResumeCount);
    }

    [Fact]
    public async Task Review_EditRemoveAddMapsExactly()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);

        var draft = page.Draft!;
        await page.EventAsync("RemoveEducation", draft.Educations[0].Key);
        await page.EventAsync("RemoveWork", draft.WorkExperiences[0].Key);
        Assert.Single(draft.Educations);
        Assert.Single(draft.WorkExperiences);

        draft.Title = "Reviewed CV";
        var education = draft.Educations[0];
        education.SchoolName = "Edited University";
        education.Degree = " Bachelor   of Science ";
        education.FieldOfStudy = "Computer Science";
        education.StartDate = new DateOnly(2020, 9, 1);
        education.EndDate = new DateOnly(2024, 6, 30);
        education.Description = "Edited education";
        var work = draft.WorkExperiences[0];
        work.CompanyName = "Edited Company";
        work.JobTitle = "Senior Developer";
        work.StartDate = new DateOnly(2021, 3, 1);
        work.EndDate = new DateOnly(2030, 1, 1);
        work.IsCurrent = true;
        work.Description = "Edited work";
        page.SetField("skillPickerId", 4);
        page.SetField("skillPickerYears", 6);
        await page.EventAsync("AddSkill");

        await page.EventAsync("SaveAsync");

        Assert.Equal(before + 1, page.ResumeCount);
        var id = page.Fixture.Db.Resumes.OrderByDescending(resume => resume.Id).Select(resume => resume.Id).First();
        var stored = (await page.StoredAsync(id))!;
        Assert.Equal("Reviewed CV", stored.Title);
        var storedEducation = Assert.Single(stored.Educations);
        Assert.Equal("Edited University", storedEducation.SchoolName);
        Assert.Equal("Bachelor of Science - Computer Science", storedEducation.Major);
        Assert.Equal(new DateOnly(2020, 9, 1), storedEducation.StartDate);
        Assert.Equal(new DateOnly(2024, 6, 30), storedEducation.EndDate);
        Assert.Equal("Edited education", storedEducation.Description);
        var storedWork = Assert.Single(stored.WorkExperiences);
        Assert.Equal("Edited Company", storedWork.CompanyName);
        Assert.Equal("Senior Developer", storedWork.JobTitle);
        Assert.Equal(new DateOnly(2021, 3, 1), storedWork.StartDate);
        Assert.Null(storedWork.EndDate);
        Assert.Equal("Edited work", storedWork.Description);
        Assert.Equal(2, stored.Skills.Count);
        Assert.Contains(stored.Skills, skill => skill.SkillId == 1 && skill.YearsOfExperience == 1);
        Assert.Contains(stored.Skills, skill => skill.SkillId == 4 && skill.YearsOfExperience == 6);
    }

    [Fact]
    public async Task Save_ConcurrentAndRepeatedEventsCreateOneResume()
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Reviewed CV";

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateHeld = page.Gate.RunAsync(() => release.Task);
        Task saving = Task.CompletedTask;
        try
        {
            await page.Dispatcher.InvokeAsync(() =>
            {
                saving = page.EventAsync("SaveAsync");
            });
            Assert.False(saving.IsCompleted);
            await page.EventAsync("SaveAsync");
            Assert.Equal(before, page.ResumeCount);
        }
        finally
        {
            release.SetResult();
            await gateHeld;
            await saving;
        }

        Assert.Equal(before + 1, page.ResumeCount);
        await page.EventAsync("SaveAsync");
        Assert.Equal(before + 1, page.ResumeCount);
    }

    [Fact]
    public async Task Attach_OptOutAndOptInUseCorrectSource()
    {
        await using var optedOut = await ReviewPage.CreateAsync();
        await optedOut.SelectFileAsync("cv.pdf", PdfBytes);
        await optedOut.EventAsync("ParseAsync", ResumeParseMode.Rules);
        optedOut.Draft!.Title = "Opt out";
        optedOut.SetField("attachOriginal", false);
        await optedOut.EventAsync("SaveAsync");
        Assert.Equal(0, optedOut.Storage.SaveCalls);
        Assert.Equal(1, optedOut.File!.OpenCount);
        Assert.Null((await optedOut.StoredAsync(optedOut.Fixture.Db.Resumes.Max(resume => resume.Id)))!.FileUrl);

        await using var optedIn = await ReviewPage.CreateAsync();
        await optedIn.SelectFileAsync("original.pdf", PdfBytes);
        await optedIn.EventAsync("ParseAsync", ResumeParseMode.Rules);
        optedIn.Draft!.Title = "Opt in";
        await optedIn.EventAsync("SaveAsync");
        Assert.Equal(1, optedIn.Storage.SaveCalls);
        Assert.Equal(2, optedIn.File!.OpenCount);
        var saved = Assert.Single(optedIn.Storage.Saved);
        Assert.Equal("original.pdf", saved.Name);
        Assert.Equal(PdfBytes, saved.Bytes);
        Assert.NotNull((await optedIn.StoredAsync(optedIn.Fixture.Db.Resumes.Max(resume => resume.Id)))!.FileUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Attach_ResultOrExceptionFailurePreservesResumeAndDraft(bool throws)
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        if (throws)
            page.Storage.OnSave = (_, _, _) => throw new IOException("provider failed");
        else
            page.Storage.OnSave = (_, _, _) => Task.FromResult(ServiceResult<string>.Fail(
                ServiceErrorType.Validation, "This file could not be stored."));
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Partial success";
        await page.EventAsync("SaveAsync");

        Assert.Equal(before + 1, page.ResumeCount);
        Assert.Equal(0, page.Storage.DeleteCalls);
        Assert.NotNull(page.Draft);
        Assert.Equal("Partial success", page.Draft.Title);
        var html = await page.HtmlAsync();
        Assert.Contains("Resume saved, but the original file was not attached.", html);
        Assert.DoesNotContain("provider failed", html);
        if (!throws) Assert.Contains("This file could not be stored.", html);
        Assert.Single(page.SavedMessages);
        Assert.Contains("Resume saved, but the original file was not attached.", page.SavedMessages[0]);

        await page.EventAsync("SaveAsync");
        Assert.Equal(before + 1, page.ResumeCount);
    }

    [Fact]
    public async Task CreateOrRefreshFailureShowsHonestErrors()
    {
        var catalog = new List<SkillOptionDto>
        {
            new(1, "C#", null), new(2, ".NET", null), new(3, "Java", null),
            new(4, "SQL", null), new(999, "Ghost", null)
        };
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { Catalog = catalog });
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Service validation";
        foreach (var key in page.Draft.Skills.Select(skill => skill.Key).ToArray())
            await page.EventAsync("RemoveSkill", key);
        page.SetField("skillPickerId", 999);
        page.SetField("skillPickerYears", 1);
        await page.EventAsync("AddSkill");
        await page.EventAsync("SaveAsync");

        Assert.Equal(before, page.ResumeCount);
        Assert.Contains("One or more selected skills no longer exist.", await page.HtmlAsync());
        Assert.NotNull(page.Draft);

        await using var refreshing = await ReviewPage.CreateAsync(new ReviewOptions { SavedCallbackThrows = true });
        var refreshBefore = refreshing.ResumeCount;
        await refreshing.SelectFileAsync("cv.pdf", PdfBytes);
        await refreshing.EventAsync("ParseAsync", ResumeParseMode.Rules);
        refreshing.Draft!.Title = "Refresh fails";
        await refreshing.EventAsync("SaveAsync");

        Assert.Equal(refreshBefore + 1, refreshing.ResumeCount);
        Assert.Contains("Resume saved, but the CV list could not be refreshed.", await refreshing.HtmlAsync());
        await refreshing.EventAsync("SaveAsync");
        Assert.Equal(refreshBefore + 1, refreshing.ResumeCount);
    }

    [Fact]
    public async Task Dispose_CancelsAndIgnoresLateParse()
    {
        ParsedCall? captured = null;
        var release = new TaskCompletionSource<ServiceResult<ParsedResumeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            AiAvailable = true,
            ParseHandler = call =>
            {
                captured = call;
                return release.Task;
            }
        });
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        Task pending = Task.CompletedTask;
        await page.Dispatcher.InvokeAsync(() =>
        {
            pending = page.EventAsync("ParseAsync", ResumeParseMode.Ai);
        });
        await page.Dispatcher.InvokeAsync(() => { });

        Assert.NotNull(captured);
        ((IDisposable)page.Child!).Dispose();
        Assert.True(captured!.Token.IsCancellationRequested);

        release.SetResult(ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed()));
        await pending;

        Assert.Null(page.Draft);
        Assert.Empty(page.SavedMessages);
        Assert.Equal(0, page.CancelCount);
    }

    [Fact]
    public async Task ReplaceSuggestions_RequiresExplicitConfirmation()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        page.Draft!.Title = "Edited suggestions";

        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        Assert.Single(page.Parser.Calls);
        Assert.Equal("Edited suggestions", page.Draft.Title);
        Assert.Contains("Replace the current suggestions", await page.HtmlAsync());

        await page.EventAsync("ConfirmReplaceAsync");
        Assert.Equal(2, page.Parser.Calls.Count);
        Assert.Equal(string.Empty, page.Draft.Title);
    }

    [Fact]
    public async Task Profile_GuardAndExistingSectionsRemainIntact()
    {
        await using var candidate = await ReviewPage.CreateAsync(new ReviewOptions
        {
            RenderProfile = true, Role = "Candidate"
        });
        await candidate.ProfileEventAsync("OpenCreateChoice");
        var candidateHtml = await candidate.HtmlAsync();
        Assert.Contains("Create resume from a CV file", candidateHtml);
        Assert.Contains("class=\"profile-summary\"", candidateHtml);
        Assert.Contains("id=\"change-password\"", candidateHtml);
        await candidate.ProfileEventAsync("StartCvReview");
        Assert.NotNull(candidate.Child);
        Assert.Contains("Review parsed CV", await candidate.HtmlAsync());

        await using var employer = await ReviewPage.CreateAsync(new ReviewOptions
        {
            RenderProfile = true, Role = "Employer"
        });
        Assert.DoesNotContain("Create resume from a CV file", await employer.HtmlAsync());

        await using var anonymous = await ReviewPage.CreateAsync(new ReviewOptions
        {
            RenderProfile = true, Role = null
        });
        Assert.DoesNotContain("Create resume from a CV file", await anonymous.HtmlAsync());
    }

    [Theory]
    [InlineData("cv.doc", 16L, "Only PDF and DOCX files are supported")]
    [InlineData("cv.pdf", 0L, "The selected file is empty")]
    [InlineData("cv.pdf", 6L * 1024 * 1024, "CV files must be 5 MB or smaller")]
    public async Task FileSelection_RejectsUnsupportedEmptyAndOversize(string name, long size, string expected)
    {
        await using var page = await ReviewPage.CreateAsync();
        var before = page.ResumeCount;
        await page.SelectFileAsync(name, PdfBytes, size);
        var html = await page.HtmlAsync();
        Assert.Contains(expected, html);
        Assert.Empty(page.Parser.Calls);
        Assert.Equal(before, page.ResumeCount);

        await page.EventAsync("StartManual");
        Assert.NotNull(page.Draft);
    }

    [Theory]
    [InlineData("scanned", "No readable text was found")]
    [InlineData("corrupt", "This file could not be read")]
    [InlineData("busy", "Too many parse requests")]
    public async Task ParserFailures_ShowFriendlyErrorsAndKeepManualEntry(string kind, string expected)
    {
        var result = kind switch
        {
            "scanned" => ServiceResult<ParsedResumeDto>.Fail(
                ServiceErrorType.Validation, "No readable text was found in this file. Try another file or enter your CV manually."),
            "corrupt" => ServiceResult<ParsedResumeDto>.Fail(
                ServiceErrorType.Validation, "This file could not be read. It may be protected or corrupt."),
            _ => ServiceResult<ParsedResumeDto>.Fail(
                ServiceErrorType.Conflict, "Too many parse requests. Please try again shortly.")
        };
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { ParseResult = result });
        var before = page.ResumeCount;
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);

        Assert.Contains(expected, await page.HtmlAsync());
        Assert.Equal(before, page.ResumeCount);
        await page.EventAsync("StartManual");
        Assert.NotNull(page.Draft);
    }
}
