using Reacher.WebsiteAnalysis.Analysis;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class WebsiteFetchTests
{
    private const string Page = """
        <!DOCTYPE html>
        <html><head>
        <meta name="viewport" content="width=device-width, initial-scale=1">
        </head><body><footer>© 2026 Example</footer></body></html>
        """;

    [Fact]
    public async Task Final_https_url_sets_the_https_check()
    {
        using var harness = AnalysisHarness.Create(
            page: (_, _) => Task.FromResult(AnalysisHarness.Html(Page)),
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json(Fixture.Read("pagespeed-ok.json"))));

        var result = await harness.Analyzer.AnalyzeAsync(
            "https://example.test/",
            CancellationToken.None);

        Assert.True(result.Checks.Https);
        Assert.True(result.Checks.Viewport);
        Assert.Equal(2026, result.Checks.CopyrightYear);
        Assert.InRange(result.Checks.ResponseTimeMs, 0, 10_000);
        Assert.Equal("https://example.test/", result.FinalUrl.AbsoluteUri);
    }

    [Fact]
    public async Task Final_http_url_fails_the_https_check()
    {
        using var harness = AnalysisHarness.Create(
            page: (_, _) => Task.FromResult(AnalysisHarness.Html("<body>Example</body>")),
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json("{}")));

        var result = await harness.Analyzer.AnalyzeAsync(
            "http://example.test/",
            CancellationToken.None);

        Assert.False(result.Checks.Https);
    }

    [Fact]
    public async Task A_relative_redirect_is_followed_and_validated()
    {
        using var harness = AnalysisHarness.Create(
            page: (request, _) =>
            {
                if (request.RequestUri?.AbsolutePath == "/")
                {
                    var redirect = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("/page", UriKind.Relative);
                    return Task.FromResult(redirect);
                }

                return Task.FromResult(AnalysisHarness.Html(Page));
            },
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json("{}")));

        var result = await harness.Analyzer.AnalyzeAsync(
            "https://example.test/",
            CancellationToken.None);

        Assert.Equal("https://example.test/page", result.FinalUrl.AbsoluteUri);
        Assert.Equal(2, harness.Page.Calls);
    }

    [Fact]
    public async Task Five_redirects_then_a_page_is_allowed()
    {
        using var harness = AnalysisHarness.Create(
            page: RedirectsThenOk(WebsitePageFetcher.MaxRedirects),
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json("{}")));

        var result = await harness.Analyzer.AnalyzeAsync(
            "https://example.test/",
            CancellationToken.None);

        Assert.Equal("https://example.test/hop-5", result.FinalUrl.AbsoluteUri);
        Assert.Equal(WebsitePageFetcher.MaxRedirects + 1, harness.Page.Calls);
    }

    [Fact]
    public async Task A_sixth_redirect_is_rejected()
    {
        using var harness = AnalysisHarness.Create(
            page: RedirectsThenOk(WebsitePageFetcher.MaxRedirects + 1),
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json("{}")));

        var exception = await Assert.ThrowsAsync<AnalysisException>(() =>
            harness.Analyzer.AnalyzeAsync("https://example.test/", CancellationToken.None));

        Assert.Equal(AnalysisException.InvalidUrlCode, exception.Code);
        Assert.Equal(WebsitePageFetcher.MaxRedirects + 1, harness.Page.Calls);
    }

    [Fact]
    public async Task The_body_is_capped_at_two_megabytes()
    {
        var html = "<meta name=\"viewport\" content=\"width=device-width\">"
            + new string('x', WebsitePageFetcher.MaxBodyBytes)
            + "<footer>© 2024</footer>";
        using var harness = AnalysisHarness.Create(
            page: (_, _) => Task.FromResult(AnalysisHarness.Html(html)),
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json("{}")));

        var result = await harness.Analyzer.AnalyzeAsync(
            "https://example.test/",
            CancellationToken.None);

        Assert.True(result.Checks.Viewport);
        Assert.Null(result.Checks.CopyrightYear);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> RedirectsThenOk(
        int redirectCount)
    {
        var sends = 0;
        return (_, _) =>
        {
            sends++;
            if (sends <= redirectCount)
            {
                var redirect = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri($"https://example.test/hop-{sends}");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(AnalysisHarness.Html("<body>Example</body>"));
        };
    }
}
