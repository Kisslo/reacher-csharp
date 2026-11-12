using Reacher.WebsiteAnalysis.Brave;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class FakeBraveSearchClientTests
{
    [Fact]
    public async Task SearchAsync_returns_a_made_up_deterministic_result()
    {
        var client = new FakeBraveSearchClient();

        var results = await client.SearchAsync("Example Company", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("https://example.test/company", result.Url.AbsoluteUri);
        Assert.Equal("Example Company", result.Title);
    }

    [Fact]
    public async Task SearchAsync_honors_cancellation_before_returning_results()
    {
        var client = new FakeBraveSearchClient();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.SearchAsync("Example Company", cancellationSource.Token));
    }

    [Theory]
    [InlineData(FakeBraveSearchClient.NoWebsiteCompanyName)]
    [InlineData("\"No Website Company\"")]
    [InlineData("\"No Website Company\" Teststad website")]
    [InlineData("\"no website company\" Göteborg website")]
    public async Task SearchAsync_returns_no_results_for_the_no_website_fixture(string query)
    {
        var client = new FakeBraveSearchClient();

        var results = await client.SearchAsync(query, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_keeps_a_longer_quoted_name()
    {
        var client = new FakeBraveSearchClient();

        var results = await client.SearchAsync(
            "\"No Website Company Extra\" Malmö website",
            CancellationToken.None);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task SearchAsync_returns_configured_fixture_results()
    {
        IReadOnlyList<BraveSearchResult> fixture =
        [
            new(
                new Uri("https://fixture.test/company"),
                "Fixture Company",
                "Made-up fixture result."),
            new(
                new Uri("https://fixture.test/company-2"),
                "Fixture Company 2",
                "Made-up fixture result."),
            new(
                new Uri("https://fixture.test/company-3"),
                "Fixture Company 3",
                "Made-up fixture result."),
            new(
                new Uri("https://fixture.test/company-4"),
                "Fixture Company 4",
                "Made-up fixture result."),
        ];
        var client = new FakeBraveSearchClient(
            new Dictionary<string, IReadOnlyList<BraveSearchResult>>
            {
                ["Fixture Company"] = fixture,
            });

        var results = await client.SearchAsync("Fixture Company", CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal(fixture.Take(3), results);
    }
}
