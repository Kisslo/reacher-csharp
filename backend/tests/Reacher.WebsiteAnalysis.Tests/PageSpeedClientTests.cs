using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Reacher.WebsiteAnalysis.Analysis;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class PageSpeedClientTests
{
    [Fact]
    public async Task Fixture_response_maps_the_four_category_scores()
    {
        var handler = new CallbackHandler((_, _) =>
            Task.FromResult(AnalysisHarness.Json(Fixture.Read("pagespeed-ok.json"))));
        using var http = new HttpClient(handler);
        var scores = await Client(http).GetScoresAsync(
            new Uri("https://example.test/"),
            CancellationToken.None);

        Assert.NotNull(scores);
        Assert.Equal(40, scores.Performance);
        Assert.Equal(50, scores.Accessibility);
        Assert.Equal(60, scores.BestPractices);
        Assert.Equal(70, scores.Seo);

        var query = AnalysisHarness.ParseQuery(handler.LastQuery);
        Assert.Equal("mobile", Assert.Single(query["strategy"]));
        Assert.Equal(
            ["performance", "accessibility", "best-practices", "seo"],
            query["category"]);
        Assert.Equal("https://example.test/", Assert.Single(query["url"]));
        Assert.Equal(AnalysisHarness.ApiKey, Assert.Single(query["key"]));
    }

    [Fact]
    public async Task A_missing_category_score_returns_null()
    {
        const string Json = """
            {"lighthouseResult":{"categories":{"performance":{"score":0.5}}}}
            """;
        var handler = new CallbackHandler((_, _) => Task.FromResult(AnalysisHarness.Json(Json)));
        using var http = new HttpClient(handler);

        var scores = await Client(http).GetScoresAsync(
            new Uri("https://example.test/"),
            CancellationToken.None);

        Assert.Null(scores);
    }

    [Fact]
    public async Task An_http_error_returns_null_and_does_not_log_the_url_or_key()
    {
        var logger = new ListLogger<PageSpeedClient>();
        var handler = new CallbackHandler((_, _) =>
            Task.FromResult(AnalysisHarness.Json(
                """{"marker":"BODY-MARKER"}""",
                System.Net.HttpStatusCode.InternalServerError)));
        using var http = new HttpClient(handler);

        var scores = await Client(http, logger).GetScoresAsync(
            new Uri("https://example.test/home"),
            CancellationToken.None);

        Assert.Null(scores);
        Assert.Contains(logger.Messages, message => message.Contains("500", StringComparison.Ordinal));
        AssertNoSecrets(logger);
    }

    private static PageSpeedClient Client(HttpClient http, ILogger<PageSpeedClient>? logger = null) =>
        new(
            http,
            OptionsFactory.Create(new PageSpeedOptions
            {
                ApiKey = AnalysisHarness.ApiKey,
                Strategy = "mobile",
                TimeoutSeconds = 90,
            }),
            logger ?? NullLogger<PageSpeedClient>.Instance);

    private static void AssertNoSecrets(ListLogger<PageSpeedClient> logger)
    {
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("example.test", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains(AnalysisHarness.ApiKey, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("BODY-MARKER", StringComparison.Ordinal));
    }
}
