using System.Net;
using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Brave;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class BraveSearchClientTests
{
    [Fact]
    public async Task SearchAsync_sends_authenticated_request_and_parses_at_most_three_results()
    {
        var handler = new RecordingHandler(
            """
            {
              "web": {
                "results": [
                  { "url": "https://example.test/one", "title": "One", "description": "First" },
                  { "url": "https://example.test/two", "title": "Two", "description": "Second" },
                  { "url": "https://example.test/three", "title": "Three", "description": "Third" },
                  { "url": "https://example.test/four", "title": "Four", "description": "Fourth" }
                ]
              }
            }
            """);
        var client = CreateClient(handler);

        var results = await client.SearchAsync("Example Company", CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal("https://example.test/one", results[0].Url.AbsoluteUri);
        Assert.Equal("One", results[0].Title);
        Assert.Equal("First", results[0].Description);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(
            "/res/v1/web/search?q=Example%20Company&count=3&country=se&search_lang=sv",
            handler.RequestUri?.PathAndQuery);
        Assert.Equal("test-api-key", handler.AuthorizationToken);
    }

    [Fact]
    public async Task SearchAsync_allows_missing_optional_fields()
    {
        var handler = new RecordingHandler(
            """{"web":{"results":[{"url":"https://example.test"}]}}""");
        var client = CreateClient(handler);

        var result = Assert.Single(await client.SearchAsync("Example", CancellationToken.None));

        Assert.Null(result.Title);
        Assert.Null(result.Description);
    }

    [Fact]
    public async Task SearchAsync_throws_without_exposing_response_details_on_http_failure()
    {
        var handler = new RecordingHandler(
            """{"error":"private response details"}""",
            HttpStatusCode.TooManyRequests);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BraveSearchException>(() =>
            client.SearchAsync("Example", CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.DoesNotContain("private response details", exception.ToString());
    }

    [Fact]
    public async Task SearchAsync_throws_for_invalid_json()
    {
        var handler = new RecordingHandler("{");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BraveSearchException>(() =>
            client.SearchAsync("Example", CancellationToken.None));

        Assert.Contains("invalid response", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_rejects_blank_company_names()
    {
        var handler = new RecordingHandler("{}");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SearchAsync(" ", CancellationToken.None));

        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task SearchAsync_throws_when_monthly_quota_is_exceeded()
    {
        var handler = new RecordingHandler("{}");
        var options = Microsoft.Extensions.Options.Options.Create(new BraveOptions
        {
            ApiKey = "test-api-key",
        });
        var usageCounter = new AlwaysDenyUsageCounter();
        var client = new BraveSearchClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.search.brave.com/"),
            },
            options,
            new BraveRequestRateLimiter(options, TimeProvider.System),
            usageCounter);

        await Assert.ThrowsAsync<BraveQuotaExceededException>(() =>
            client.SearchAsync("Example", CancellationToken.None));

        Assert.Null(handler.RequestUri);
    }

    private static BraveSearchClient CreateClient(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.search.brave.com/"),
        };
        var options = Microsoft.Extensions.Options.Options.Create(new BraveOptions
        {
            ApiKey = "test-api-key",
        });
        var rateLimiter = new BraveRequestRateLimiter(
            options,
            TimeProvider.System);
        var usageCounter = new AlwaysAllowUsageCounter();

        return new BraveSearchClient(httpClient, options, rateLimiter, usageCounter);
    }

    private sealed class RecordingHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? AuthorizationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationToken = request.Headers.GetValues("X-Subscription-Token").Single();

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class AlwaysAllowUsageCounter : IBraveUsageCounter
    {
        public Task<bool> TryConsumeAsync(
            int monthlyCap,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class AlwaysDenyUsageCounter : IBraveUsageCounter
    {
        public Task<bool> TryConsumeAsync(
            int monthlyCap,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
