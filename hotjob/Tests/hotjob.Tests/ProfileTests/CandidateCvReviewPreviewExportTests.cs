using hotjob.DTOs.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support;

namespace hotjob.Tests;

/// <summary>
/// Renders all ten CV-review states from the REAL CandidateCvReview component
/// with a fake parser and SQLite, and asserts each state marker on every run.
/// HOTJOB_UI_PREVIEW_DIR only enables writing static HTML fixtures for visual
/// inspection: no app startup, no network, no scripts and no real AI. The
/// fixtures are static snapshots inside a fake profile shell, not live Blazor.
/// </summary>
public sealed class CandidateCvReviewPreviewExportTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 fake preview cv bytes"u8.ToArray();
    private const string EnvName = "HOTJOB_UI_PREVIEW_DIR";
    private const string LongFileName = "a-very-long-cv-file-name-used-to-check-wrapping-in-the-source-and-selected-panels.pdf";
    private const string LongWarning =
        "<script>alert('preview')</script> This parsed warning is intentionally very long so the review panel wrapping can be inspected in a static fixture.";
    private const string LongUnknown = "an-unrecognized-token-with-a-deliberately-long-name-for-wrapping";

    [Fact]
    public async Task PreviewStates_RenderExpectedMarkers_AndExportWhenOptedIn()
    {
        var directory = Environment.GetEnvironmentVariable(EnvName);
        var write = !string.IsNullOrWhiteSpace(directory);
        if (write) Directory.CreateDirectory(directory!);
        var css = write ? InlineCss() : string.Empty;

        var catalog = new List<SkillOptionDto>
        {
            new(1, "C#", null), new(2, ".NET", null),
            new(3, "Java", null), new(4, "SQL", null)
        };

        await VerifyAsync(directory, css, write, "01-empty", new ReviewOptions { Catalog = catalog },
            page => Task.FromResult<Func<Task>?>(null), html =>
            {
                AssertNoMode(html);
                Assert.Contains("Choose a PDF or DOCX file", html);
                Assert.Contains("cv-review-footer", html);
            });

        await VerifyAsync(directory, css, write, "02-selected", new ReviewOptions
        {
            AiAvailable = true, Catalog = catalog
        }, async page =>
        {
            await page.SelectFileAsync(LongFileName, PdfBytes);
            return null;
        }, html =>
        {
            AssertNoMode(html);
            Assert.Contains("Selected:", html);
            Assert.Contains("a-very-long-cv-file-name", html);
            Assert.Contains(">PDF</span>", html);
        });

        var loadingRelease = new TaskCompletionSource<ServiceResult<ParsedResumeDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await VerifyAsync(directory, css, write, "03-loading", new ReviewOptions
        {
            AiAvailable = true, Catalog = catalog, ParseHandler = _ => loadingRelease.Task
        }, async page =>
        {
            await page.SelectFileAsync(LongFileName, PdfBytes);
            var pending = Task.CompletedTask;
            await page.Dispatcher.InvokeAsync(() =>
            {
                pending = page.EventAsync("ParseAsync", ResumeParseMode.Rules);
            });
            return async () =>
            {
                loadingRelease.TrySetResult(ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed()));
                await pending;
            };
        }, html =>
        {
            AssertNoMode(html);
            Assert.Contains("aria-busy=\"true\"", html);
            Assert.Contains("Parsing CV...", html);
        });

        await VerifyAsync(directory, css, write, "04-failure", new ReviewOptions
        {
            Catalog = catalog,
            ParseResult = ServiceResult<ParsedResumeDto>.Fail(
                ServiceErrorType.Validation,
                "This file could not be read. It may be protected or corrupt. Please try another file or enter your CV manually.")
        }, async page =>
        {
            await page.SelectFileAsync(LongFileName, PdfBytes);
            await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
            return null;
        }, html =>
        {
            AssertNoMode(html);
            Assert.Contains("This file could not be read", html);
        });

        await VerifyAsync(directory, css, write, "05-manual", new ReviewOptions { Catalog = catalog },
            async page =>
            {
                await page.EventAsync("StartManual");
                await page.EventAsync("AddEducation");
                await page.EventAsync("AddWork");
                page.SetField("skillPickerId", 1);
                page.SetField("skillPickerYears", 3);
                await page.EventAsync("AddSkill");
                return null;
            }, html =>
            {
                AssertNoMode(html);
                Assert.Contains(">Manual entry</p>", html);
            });

        var multiple = ReviewSamples.Parsed() with
        {
            Warnings = [LongWarning, "A second short warning is also preserved."],
            UnrecognizedSkills = [LongUnknown],
            Skills =
            [
                new(new() { SkillId = 1, YearsOfExperience = 1 }, "C#", ResumeParseConfidence.High),
                new(new() { SkillId = 2, YearsOfExperience = 3 }, ".NET", ResumeParseConfidence.Medium),
                new(new() { SkillId = 3, YearsOfExperience = 2 }, "Java", ResumeParseConfidence.Low)
            ]
        };
        await VerifyAsync(directory, css, write, "06-rules-multiple", new ReviewOptions
        {
            Catalog = catalog, ParseResult = ServiceResult<ParsedResumeDto>.Ok(multiple)
        }, async page =>
        {
            await page.SelectFileAsync(LongFileName, PdfBytes);
            await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
            return null;
        }, html =>
        {
            Assert.Contains("Mode used: Rules", html);
            Assert.Contains("<dt>Education entries</dt><dd>2</dd>", html);
            Assert.Contains("<dt>Work experience entries</dt><dd>2</dd>", html);
            Assert.Contains("<dt>Skills</dt><dd>3</dd>", html);
            Assert.Equal(3, html.Split("cv-review-skill-row\"").Length - 1);
            Assert.Contains("<details class=\"cv-review-notices\"", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("This parsed warning is intentionally very long", html);
            Assert.Contains("A second short warning is also preserved.", html);
            Assert.Contains(LongUnknown, html);
            Assert.DoesNotMatch("<option[^>]*>" + LongUnknown, html);
        });

        var ai = ReviewSamples.Parsed() with { ModeUsed = ResumeParseMode.Ai };
        await VerifyAsync(directory, css, write, "07-ai", new ReviewOptions
        {
            AiAvailable = true, Catalog = catalog, ParseResult = ServiceResult<ParsedResumeDto>.Ok(ai)
        }, async page =>
        {
            await page.SelectFileAsync(LongFileName, PdfBytes);
            await page.EventAsync("ParseAsync", ResumeParseMode.Ai);
            return null;
        }, html => Assert.Contains("Mode used: Ai", html));

        await VerifyAsync(directory, css, write, "08-fallback", new ReviewOptions
        {
            AiAvailable = true, Catalog = catalog,
            ParseResult = ServiceResult<ParsedResumeDto>.Ok(ReviewSamples.Parsed())
        }, async page =>
        {
            await page.SelectFileAsync(LongFileName, PdfBytes);
            await page.EventAsync("ParseAsync", ResumeParseMode.Ai);
            return null;
        }, html =>
        {
            Assert.Contains("Mode used: Rules", html);
            Assert.Contains("AI was unavailable; local rules were used.", html);
        });

        await VerifyAsync(directory, css, write, "09-validation", new ReviewOptions { Catalog = catalog },
            async page =>
            {
                await page.SelectFileAsync(LongFileName, PdfBytes);
                await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
                page.Draft!.Title = "   ";
                await page.EventAsync("SaveAsync");
                return null;
            }, html =>
            {
                Assert.Contains("Mode used: Rules", html);
                Assert.Contains("Resume title is required.", html);
            });

        await VerifyAsync(directory, css, write, "10-partial-locked", new ReviewOptions { Catalog = catalog },
            async page =>
            {
                await page.SelectFileAsync(LongFileName, PdfBytes);
                await page.EventAsync("ParseAsync", ResumeParseMode.Rules);
                page.Draft!.Title = "Partially saved CV";
                page.Storage.OnSave = (_, _, _) => Task.FromResult(ServiceResult<string>.Fail(
                    ServiceErrorType.Validation, "This file could not be stored."));
                await page.EventAsync("SaveAsync");
                return null;
            }, html =>
            {
                Assert.Contains("Mode used: Rules", html);
                Assert.Contains("locked to prevent duplicate creation", html);
                Assert.Contains("Resume saved, but the original file was not attached.", html);
            });
    }

    private static void AssertNoMode(string html)
    {
        Assert.DoesNotContain("Mode used:", html);
        Assert.DoesNotContain("cv-review-counts", html);
    }

    private static async Task VerifyAsync(string? directory, string css, bool write, string name,
        ReviewOptions options, Func<ReviewPage, Task<Func<Task>?>> prepare, Action<string> assert)
    {
        await using var page = await ReviewPage.CreateAsync(options);
        Func<Task>? cleanup = null;
        try
        {
            cleanup = await prepare(page);
            var html = await page.HtmlAsync();
            assert(html);
            if (write)
                await File.WriteAllTextAsync(Path.Combine(directory!, name + ".html"), Wrap(css, html, name));
        }
        finally
        {
            if (cleanup is not null) await cleanup();
        }
    }

    private static string Wrap(string css, string childHtml, string name) =>
        "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\" />\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
        "<title>" + name + " - CV review static preview</title>\n" +
        "<style>\n" + css + "\n</style>\n</head>\n<body>\n" +
        "<div class=\"content-shell\">\n" +
        "  <div class=\"profile-layout\">\n" +
        "    <aside class=\"profile-sidebar\" aria-label=\"Fake preview sidebar\">\n" +
        "      <div class=\"profile-card\">\n" +
        "        <div class=\"profile-summary\"><h2>Match Candidate</h2><p>Candidate</p></div>\n" +
        "        <nav class=\"profile-nav\" aria-label=\"Fake preview sections\">" +
        "<a href=\"#\">Personal details</a><a class=\"active\" href=\"#\">Manage CVs</a>" +
        "<a href=\"#\">Career preferences</a><a href=\"#\">Change password</a></nav>\n" +
        "      </div>\n" +
        "    </aside>\n" +
        "    <main class=\"profile-content\">\n" +
        "      <section class=\"profile-box\">\n" +
        "        <div class=\"profile-box-heading\"><div><h2>CV &amp; resume management</h2>" +
        "<p>Fake profile shell for static layout inspection only.</p></div>" +
        "<span class=\"profile-badge\">FAKE PREVIEW</span></div>\n" +
        childHtml + "\n" +
        "      </section>\n" +
        "    </main>\n" +
        "  </div>\n" +
        "</div>\n</body>\n</html>\n";

    private static string InlineCss()
    {
        var root = FindRoot();
        string[] files =
        [
            "hotjob/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css",
            "hotjob/wwwroot/app.css",
            "hotjob/wwwroot/css/Workspace.css",
            "hotjob/wwwroot/css/MainLayout.css",
            "hotjob/wwwroot/css/Profile.css",
            "hotjob/wwwroot/css/Polish.css",
            "hotjob/wwwroot/css/Theme.css"
        ];
        return string.Join("\n", files.Select(file => File.ReadAllText(Path.Combine(root, file))));
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
