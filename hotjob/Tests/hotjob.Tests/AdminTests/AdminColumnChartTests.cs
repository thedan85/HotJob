using System.Text.RegularExpressions;
using hotjob.Components.Pages.Admin;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.AdminTests;

/// <summary>Markup checks for the reports page column chart.</summary>
public sealed class AdminColumnChartTests
{
    private static async Task<string> RenderAsync(IReadOnlyList<AdminColumnChart.ChartPoint> points)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AdminColumnChart>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(AdminColumnChart.Title)] = "Applications per month",
                [nameof(AdminColumnChart.Points)] = points
            }));
            return output.ToHtmlString();
        });

        // Opt-in visual preview: HOTJOB_CHART_PREVIEW=<folder> writes the rendered chart there.
        if (Environment.GetEnvironmentVariable("HOTJOB_CHART_PREVIEW") is { Length: > 0 } folder)
        {
            var css = File.Exists(Path.Combine(folder, "Workspace.css")) ? "<link rel=\"stylesheet\" href=\"Workspace.css\">" : string.Empty;
            await File.WriteAllTextAsync(
                Path.Combine(folder, $"chart-{points.Count}.html"),
                $"<!doctype html><meta charset=\"utf-8\">{css}<body style=\"font-family:sans-serif;padding:24px;max-width:800px\">{html}</body>");
        }

        return html;
    }

    [Fact]
    public async Task OneColumnPerMonth_ValueOnTallestOnly_TooltipOnEvery()
    {
        var html = await RenderAsync(
        [
            new("Apr 26", 4),
            new("May 26", 0),
            new("Jun 26", 9),
            new("Jul 26", 2)
        ]);

        Assert.Equal(4, Regex.Matches(html, "class=\"admin-chart-slot\"").Count);
        Assert.Equal(3, Regex.Matches(html, "class=\"admin-chart-bar\"").Count); // the zero month draws no bar
        var values = Regex.Matches(html, "class=\"admin-chart-value\"[^>]*>([^<]*)<");
        Assert.Equal("9", Assert.Single(values).Groups[1].Value);
        Assert.Contains("<title>Jun 26: 9</title>", html);
        Assert.Contains("<title>May 26: 0</title>", html);
        Assert.Contains("aria-label=\"Applications per month. Apr 26 4, May 26 0, Jun 26 9, Jul 26 2\"", html);
    }

    [Fact]
    public async Task TwelveMonths_RenderAtNaturalSize()
    {
        var points = Enumerable.Range(1, 12)
            .Select(month => new AdminColumnChart.ChartPoint(new DateTime(2026, month, 1).ToString("MMM yy"), month * 3 % 7 + 1))
            .ToList();

        var html = await RenderAsync(points);

        // 12 slots x 44px; the SVG keeps this size and only shrinks (CSS max-width) on narrow screens.
        Assert.Contains("width=\"528\"", html);
        Assert.Equal(12, Regex.Matches(html, "class=\"admin-chart-bar\"").Count);
    }

    [Fact]
    public async Task AllZero_DrawsNoBarsOrValue()
    {
        var html = await RenderAsync([new("Apr 26", 0), new("May 26", 0)]);

        Assert.DoesNotContain("admin-chart-bar\"", html);
        Assert.DoesNotContain("admin-chart-value", html);
        Assert.Equal(2, Regex.Matches(html, "class=\"admin-chart-label\"").Count);
    }

    [Fact]
    public async Task NoPoints_ShowsNoData()
    {
        var html = await RenderAsync([]);

        Assert.Contains("No data.", html);
        Assert.DoesNotContain("<svg", html);
    }
}
