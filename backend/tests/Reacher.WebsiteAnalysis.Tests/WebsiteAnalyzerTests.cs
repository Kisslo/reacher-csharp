using Reacher.WebsiteAnalysis.Analysis;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class WebsiteAnalyzerTests
{
    private const string HealthyPage = """
        <!DOCTYPE html>
        <html><head>
        <meta name="viewport" content="width=device-width, initial-scale=1">
        </head><body><footer>© 2026 Example</footer></body></html>
        """;

    [Fact]
    public async Task PageSpeed_and_own_checks_produce_one_score()
    {
        using var harness = AnalysisHarness.Create(
            page: (_, _) => Task.FromResult(AnalysisHarness.Html(HealthyPage)),
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json(Fixture.Read("pagespeed-ok.json"))));

        var result = await harness.Analyzer.AnalyzeAsync(
            "https://example.test/",
            CancellationToken.None);

        Assert.Equal(48, result.ReworkScore);
        Assert.False(result.ScoreFromChecksOnly);
        Assert.NotNull(result.Lighthouse);
        Assert.Equal(40, result.Lighthouse.Performance);
        Assert.Equal(50, result.Lighthouse.Accessibility);
        Assert.Equal(60, result.Lighthouse.BestPractices);
        Assert.Equal(70, result.Lighthouse.Seo);
        Assert.Equal(AnalysisClock.Instant, result.FetchedAt);
        Assert.Equal(1, harness.Page.Calls);
        Assert.Equal(1, harness.PageSpeed.Calls);
    }

    [Fact]
    public async Task A_PageSpeed_error_still_returns_own_checks()
    {
        using var harness = AnalysisHarness.Create(
            page: (_, _) => Task.FromResult(AnalysisHarness.Html("<body><footer>© 2020 Example</footer></body>")),
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json(
                """{"marker":"BODY-MARKER"}""",
                System.Net.HttpStatusCode.InternalServerError)));

        var result = await harness.Analyzer.AnalyzeAsync(
            "http://example.test/",
            CancellationToken.None);

        Assert.Null(result.Lighthouse);
        Assert.True(result.ScoreFromChecksOnly);
        Assert.False(result.Checks.Https);
        Assert.False(result.Checks.Viewport);
        Assert.Equal(2020, result.Checks.CopyrightYear);
        Assert.Equal(40, result.ReworkScore);
    }

    [Fact]
    public async Task A_PageSpeed_timeout_still_returns_own_checks_without_logging_the_url()
    {
        var logger = new ListLogger<PageSpeedClient>();
        using var harness = AnalysisHarness.Create(
            page: (_, _) => Task.FromResult(AnalysisHarness.Html(HealthyPage)),
            pageSpeed: AnalysisHarness.Hang,
            pageSpeedTimeout: TimeSpan.FromMilliseconds(500),
            logger: logger);

        var result = await harness.Analyzer.AnalyzeAsync(
            "https://example.test/",
            CancellationToken.None);

        Assert.Null(result.Lighthouse);
        Assert.True(result.ScoreFromChecksOnly);
        Assert.True(result.Checks.Viewport);
        Assert.Equal(0, result.ReworkScore);
        Assert.Contains(logger.Messages, message => message.Contains("timed out", StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("example.test", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains(AnalysisHarness.ApiKey, StringComparison.Ordinal));
    }
}
