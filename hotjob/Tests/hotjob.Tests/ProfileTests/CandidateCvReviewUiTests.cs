using System.Text.RegularExpressions;
using hotjob.Components.Account;
using hotjob.DTOs.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;

namespace hotjob.Tests;

/// <summary>
/// Focused render-only contracts for the redesigned CV review. These assert
/// structure, copy and styles; all save/parse/cancel behavior stays covered by
/// the existing behavioral suites. Written before the markup/CSS change (RED).
/// </summary>
public sealed class CandidateCvReviewUiTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 test cv bytes"u8.ToArray();
    private const string Marker = "/* ---------- Candidate CV review (scoped to .cv-review) ---------- */";

    private static string ReadCss()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "hotjob.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "hotjob/wwwroot/css/Profile.css"));
    }

    private static string ReadRazor()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "hotjob.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName,
            "hotjob/Components/Account/CandidateCvReview.razor"));
    }

    private static string ReviewBlock(string css)
    {
        var index = css.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(index >= 0, "The review marker must exist.");
        return css[index..];
    }

    [Fact]
    public async Task BeforeParse_HasHeaderSourceActionsAndNoSuccessPanel()
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { AiAvailable = true });
        var html = await page.HtmlAsync();

        Assert.Contains("Review parsed CV", html);
        Assert.Contains(">Close</button>", html);
        Assert.Contains("Parsing creates suggestions only", html);
        Assert.Contains("CV file", html);
        Assert.Contains("Choose a PDF or DOCX file", html);
        Assert.Contains("up to 5 MB", html);
        Assert.Contains("Parse CV", html);
        Assert.Contains("Parse with AI", html);
        Assert.Contains("Start with an empty CV", html);
        Assert.Contains("cv-review-layout", html);
        Assert.Contains("cv-review-side", html);
        Assert.Contains("cv-review-main", html);
        Assert.Contains("cv-review-footer", html);
        Assert.DoesNotContain("Mode used:", html);
        Assert.DoesNotContain("cv-review-counts", html);
        Assert.DoesNotContain("Parsing CV...", html);
    }

    [Fact]
    public async Task SelectedFile_ShowsEncodedNameTypeAndGuidance()
    {
        const string longName = "<b>very-long-cv-file-name-for-review-display-check.pdf";
        await using var page = await ReviewPage.CreateAsync();
        await page.SelectFileAsync(longName, PdfBytes);
        var html = await page.HtmlAsync();

        Assert.Contains("Selected:", html);
        Assert.Contains("&lt;b&gt;very-long-cv-file-name-for-review-display-check.pdf", html);
        Assert.DoesNotContain("<b>very-long-cv-file-name", html);
        Assert.Contains("PDF", html);
        Assert.Contains("up to 5 MB", html);
    }

    [Fact]
    public async Task Loading_ShowsBusyStateWithoutSuccessPanel()
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
            var html = await page.HtmlAsync();
            Assert.Contains("aria-busy=\"true\"", html);
            Assert.Contains("Parsing CV...", html);
            Assert.Matches("<button[^>]*disabled[^>]*>Parse CV</button>", html);
            Assert.DoesNotContain("Mode used:", html);
            Assert.DoesNotContain("Items to review", html);
        }
        finally
        {
            release.SetResult(ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed()));
            await pending;
        }
    }

    [Fact]
    public async Task Failure_ShowsErrorWithoutSuccessPanel()
    {
        var failed = ServiceResult<ParsedResumeDto>.Fail(
            ServiceErrorType.Validation, "This file could not be read. It may be protected or corrupt.");
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions { ParseResult = failed });
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var html = await page.HtmlAsync();

        Assert.Contains("This file could not be read", html);
        Assert.DoesNotContain("Mode used:", html);
        Assert.DoesNotContain("cv-review-counts", html);
        Assert.DoesNotContain("Parsing CV...", html);
    }

    [Fact]
    public async Task Manual_ShowsManualEntryAndEditableSectionsWithoutMode()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.EventAsync("StartManual");
        await page.EventAsync("AddEducation");
        await page.EventAsync("AddWork");
        var html = await page.HtmlAsync();

        Assert.Contains(">Manual entry</p>", html);
        Assert.Contains(">manual entry</span>", html);
        Assert.DoesNotContain("Mode used:", html);
        Assert.DoesNotContain("cv-review-counts", html);
        Assert.Contains("Education", html);
        Assert.Contains("Work experience", html);
        Assert.Contains("Skills", html);
        Assert.Contains("Entry 1", html);
        Assert.Contains("Add education", html);
        Assert.Contains("Add experience", html);
    }

    [Fact]
    public async Task RulesParsed_ShowsActualModeAndCurrentReviewCounts()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var html = await page.HtmlAsync();

        Assert.Contains("Mode used: Rules", html);
        Assert.Contains("cv-review-counts", html);
        Assert.Matches("<dt>Education entries</dt><dd>2</dd>", html);
        Assert.Matches("<dt>Work experience entries</dt><dd>2</dd>", html);
        Assert.Matches("<dt>Skills</dt><dd>1</dd>", html);
        Assert.Contains("not the original extraction totals", html);
        Assert.Contains("Confidence describes the original suggestion", html);
    }

    [Fact]
    public async Task AiParsed_ShowsAiMode()
    {
        var parsed = ReviewSamples.Parsed() with { ModeUsed = ResumeParseMode.Ai };
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            AiAvailable = true, ParseResult = ServiceResult<ParsedResumeDto>.Ok(parsed)
        });
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Ai);
        var html = await page.HtmlAsync();

        Assert.Contains("Mode used: Ai", html);
        Assert.Contains("cv-review-counts", html);
    }

    [Fact]
    public async Task AiFallback_ShowsRulesModeAndFallbackNotice()
    {
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            AiAvailable = true, ParseResult = ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed())
        });
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Ai);
        var html = await page.HtmlAsync();

        Assert.Contains("Mode used: Rules", html);
        Assert.Contains("AI was unavailable; local rules were used.", html);
    }

    [Fact]
    public async Task Notices_KeepEveryWarningEncodedAndUnknownsNonSelectable()
    {
        const string warning = "<script>alert('warning')</script> A very long warning that must survive verbatim in the review panel.";
        const string unknown = "<svg>unknown-token-with-a-long-name</svg>";
        var parsed = ReviewSamples.Parsed() with
        {
            Warnings = [warning, "Second warning stays too."],
            UnrecognizedSkills = [unknown]
        };
        await using var page = await ReviewPage.CreateAsync(new ReviewOptions
        {
            ParseResult = ServiceResult<ParsedResumeDto>.Ok(parsed)
        });
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var html = await page.HtmlAsync();

        Assert.Contains("<details class=\"cv-review-notices\"", html);
        Assert.Matches("<summary><span role=\"status\" aria-live=\"polite\">Warnings and hints: 2 warnings, 1 hint</span></summary>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("alert(", html);
        Assert.Contains("A very long warning that must survive verbatim in the review panel.", html);
        Assert.Contains("Second warning stays too.", html);
        Assert.Contains("&lt;svg&gt;unknown-token-with-a-long-name&lt;/svg&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<svg>", html);
        Assert.DoesNotContain("<option value=\"0\">" + unknown, html);
    }

    [Fact]
    public async Task Metadata_ConfidenceBesideSummaryAndTitleRequiredIndicator()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var html = await page.HtmlAsync();

        Assert.Matches("<span class=\"cv-review-label-line\">(?s:.)*?Professional summary(?s:.)*?Summary confidence: High", html);
        Assert.Matches("<span class=\"cv-review-label-line\">(?s:.)*?CV title(?s:.)*?required", html);
        Assert.Matches("Summary confidence: High(?s:.)*?<textarea", html);
        Assert.Contains("Basic information", html);
        Assert.Contains("Confidence describes the original suggestion", html);
    }

    [Fact]
    public async Task SectionsAndSkills_HaveHeadsEntryLabelsAndThreeRowDescriptions()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var html = await page.HtmlAsync();

        Assert.Contains("cv-review-section-head", html);
        Assert.Contains("Entry 1", html);
        Assert.Contains("Entry 2", html);
        Assert.Contains("Major preview:", html);
        Assert.Contains("rows=\"3\"", html);
        Assert.Contains("Skill", html);
        Assert.Contains("Confidence", html);
        Assert.Contains("Years", html);
        Assert.Contains("cv-review-skill-row", html);
        Assert.Contains("Choose a skill", html);
        Assert.Contains("Add skill", html);
    }

    [Fact]
    public void Footer_IsNormalFlowWithCancelCloseSaveAndAttachment()
    {
        var source = ReadRazor();
        Assert.Contains("class=\"cv-review-footer\"", source);
        Assert.Contains("Also attach the original file to this resume", source);
        Assert.Contains("@onclick=\"CancelAsync\"", source);
        Assert.Contains("type=\"button\"", source);
        Assert.DoesNotContain("OnValidSubmit", source);
        Assert.DoesNotContain("OnInvalidSubmit", source);

        var block = ReviewBlock(ReadCss());
        Assert.Contains(".cv-review-footer", block);
        Assert.DoesNotMatch("\\.cv-review-footer(?s:.)*?position:\\s*sticky", block);
    }

    [Fact]
    public void FileControl_IsAccessibleOverlayWithoutDragDropClaim()
    {
        var source = ReadRazor();
        Assert.Contains("<InputFile", source);
        Assert.Contains("accept=\".pdf,.docx\"", source);
        Assert.DoesNotContain("drag", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop a file", source, StringComparison.OrdinalIgnoreCase);

        var block = ReviewBlock(ReadCss());
        Assert.Contains(".cv-review .cv-review-file input[type=\"file\"]", block);
        Assert.Contains("position: absolute", block);
        Assert.Contains(".cv-review-file:focus-within", block);
    }

    [Fact]
    public async Task CurrentJob_CheckboxIsInlineAndEndInputIsRemoved()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var before = (await page.HtmlAsync()).Split("type=\"date\"").Length - 1;

        page.Draft!.WorkExperiences[0].IsCurrent = true;
        await page.EventAsync("CancelReplace");
        var after = (await page.HtmlAsync()).Split("type=\"date\"").Length - 1;
        Assert.Equal(before - 1, after);

        var block = ReviewBlock(ReadCss());
        Assert.Contains(".cv-review .cv-form-row .preference-check input[type=\"checkbox\"]", block);
        Assert.Contains("width: auto", block);
        Assert.Contains("min-height: 0", block);
    }

    [Fact]
    public void ContainerLayout_UsesContainerQueryAndKeepsPrefixUntouched()
    {
        var css = ReadCss();
        var markerIndex = css.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0);
        var prefix = css[..markerIndex];
        Assert.Contains("/* ---------- CV forms ---------- */", prefix);
        Assert.Contains(".cv-form {", prefix);
        Assert.DoesNotContain(".cv-review", prefix);
        Assert.DoesNotContain("container-type", prefix);

        var block = ReviewBlock(css);
        Assert.Contains("container-type: inline-size", block);
        Assert.Contains("@container", block);
        Assert.Matches("@container[^}]*?min-width:\\s*8[0-4]0px", block);
        Assert.Contains("grid-template-columns: minmax(0, 2fr) minmax(0, 1fr)", block);
        Assert.Contains("grid-template-columns: minmax(0, 1fr)", block);
        Assert.DoesNotMatch("(?s)position:\\s*sticky", block);
    }

    [Fact]
    public async Task Layout_SourceColumnPrecedesEditorInDom()
    {
        await using var page = await ReviewPage.CreateAsync();
        var html = await page.HtmlAsync();
        Assert.True(html.IndexOf("cv-review-side", StringComparison.Ordinal) <
                    html.IndexOf("cv-review-main", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FieldGroups_UseLogicalSlotsAndPresentForCurrentJob()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var html = await page.HtmlAsync();

        Assert.Contains("cv-review-field-school", html);
        Assert.Contains("cv-review-field-degree", html);
        Assert.Contains("cv-review-field-major", html);
        Assert.Contains("cv-review-field-start", html);
        Assert.Contains("cv-review-field-end", html);
        Assert.Contains("cv-review-field-gpa", html);
        Assert.Equal(1, html.Split("cv-review-present\"").Length - 1);

        page.Draft!.WorkExperiences[0].IsCurrent = true;
        await page.EventAsync("CancelReplace");
        var currentHtml = await page.HtmlAsync();
        Assert.Contains("<span class=\"cv-review-present-label\">End</span>", currentHtml);
        Assert.Contains("<span class=\"cv-review-present-value\">Present</span>", currentHtml);
        Assert.Equal(2, currentHtml.Split("cv-review-present\"").Length - 1);
    }

    [Fact]
    public void TextareaCss_MatchesInputsAndKeepsDescriptionsCompact()
    {
        var block = ReviewBlock(ReadCss());
        Assert.Contains(".cv-review textarea", block);
        Assert.Contains("padding: 8px 10px", block);
        Assert.Contains("line-height: 1.5", block);
        Assert.Contains(".cv-review-description textarea", block);
        Assert.Contains("min-height: 80px", block);
        Assert.Contains("resize: vertical", block);
        Assert.Contains("max-width: 100%", block);
    }

    [Fact]
    public async Task SkillLabels_AreReadableAndPerRowAccessible()
    {
        await using var page = await ReviewPage.CreateAsync();
        await page.SelectFileAsync("cv.pdf", PdfBytes);
        await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
        var html = await page.HtmlAsync();

        Assert.Contains("Years of experience", html);
        Assert.Contains("<span class=\"visually-hidden\">Years of experience for C#</span>", html);
        Assert.Contains("cv-review-skill-picker", html);

        var block = ReviewBlock(ReadCss());
        Assert.Contains(".cv-review .cv-skill-picker label", block);
        Assert.Contains("grid-template-columns: minmax(0, 2fr) minmax(0, 1fr) minmax(0, 1fr) 48px", block);
        Assert.Contains("justify-self: end", block);
        Assert.Contains(".cv-review-years-short { display: none; }", block);

        var medium = Regex.Match(block,
            "@container cvreview \\(max-width: 640px\\) \\{(?<body>.*?)\\n\\}", RegexOptions.Singleline);
        Assert.True(medium.Success);
        Assert.Contains(".cv-review-years-short { display: inline; }", medium.Groups["body"].Value);
    }

    [Fact]
    public void ResponsiveCss_WrapsSectionActionsAndStacksByContainer()
    {
        var block = ReviewBlock(ReadCss());
        Assert.Contains(".cv-review-label-line", block);
        Assert.Contains("flex-wrap: wrap", block);
        Assert.Contains("@container cvreview (max-width: 420px)", block);
        Assert.Contains(".cv-review-basics-heading", block);
        Assert.Contains(".cv-review-basics {", block);

        var narrow = Regex.Match(block,
            "@container cvreview \\(max-width: 480px\\) \\{(?<body>.*?)\\n\\}", RegexOptions.Singleline);
        Assert.True(narrow.Success, "Field stacking must use a 480px container threshold.");
        var narrowBody = narrow.Groups["body"].Value;
        Assert.Contains(".cv-review .cv-form-row { grid-template-columns: minmax(0, 1fr); }", narrowBody);
        Assert.Contains(".cv-review .cv-review-entry > label,", narrowBody);
        Assert.Contains(".cv-review .cv-review-entry > span", narrowBody);
        Assert.Contains(".cv-review .cv-review-entry .cv-review-entry-head", narrowBody);
        Assert.Contains("max-width: none", narrowBody);

        var medium = Regex.Match(block,
            "@container cvreview \\(max-width: 640px\\) \\{(?<body>.*?)\\n\\}", RegexOptions.Singleline);
        Assert.True(medium.Success, "Skill wrapping must use a 640px container threshold.");
        var mediumBody = medium.Groups["body"].Value;
        Assert.Contains(".cv-review-skill-head { display: none; }", mediumBody);
        Assert.Contains(".cv-review .cv-review-skill-row .cv-remove", mediumBody);
        Assert.DoesNotContain("cv-form-row", mediumBody);

        var media = Regex.Match(block,
            "@media \\(max-width: 760px\\) \\{(?<body>.*?)\\n\\}", RegexOptions.Singleline);
        Assert.True(media.Success);
        Assert.DoesNotContain("cv-form-row", media.Groups["body"].Value);
    }

    [Fact]
    public async Task EmptyState_UsesSetupClassAndFullWidthSource()
    {
        await using var page = await ReviewPage.CreateAsync();
        var html = await page.HtmlAsync();
        Assert.Contains("cv-review--setup", html);
        Assert.Contains("aria-label=\"CV source, parsing results and review warnings\"", html);
        Assert.DoesNotContain("cv-review-basics-heading", html);

        await page.EventAsync("StartManual");
        var manualHtml = await page.HtmlAsync();
        Assert.DoesNotContain("cv-review--setup", manualHtml);
        Assert.Contains("cv-review-basics-heading", manualHtml);

        var block = ReviewBlock(ReadCss());
        Assert.Contains(".cv-review--setup .cv-review-layout", block);
        Assert.Contains(".cv-review--setup .cv-review-main { display: none; }", block);
        Assert.Contains(".cv-review--setup .cv-review-side", block);
    }

    [Fact]
    public void ResultsCounts_AreFlatRowsWithoutTintedCards()
    {
        var block = ReviewBlock(ReadCss());
        Assert.Contains(".cv-review-count-row {", block);
        Assert.DoesNotMatch("\\.cv-review-count-row[^}]*background:\\s*var\\(--hj-surface-muted\\)", block);
        Assert.Contains(".cv-review-count-row dt", block);
        Assert.Contains(".cv-review-count-row dd", block);
    }

    [Fact]
    public void SetupCss_ResetsSideGridPlacementSoNoImplicitColumnExists()
    {
        var block = ReviewBlock(ReadCss());
        Assert.Matches(
            "\\.cv-review--setup \\.cv-review-side \\{[^}]*grid-column: 1;[^}]*grid-row: auto;[^}]*\\}",
            block);
    }
}
