namespace Reacher.WebsiteAnalysis.Brave;

/// <summary>
/// Deterministic Brave replacement for development and automated tests.
/// </summary>
public sealed class FakeBraveSearchClient : IBraveSearchClient
{
    public const string NoWebsiteCompanyName = "No Website Company";

    private readonly Dictionary<string, IReadOnlyList<BraveSearchResult>> fixtures;

    public FakeBraveSearchClient(
        IReadOnlyDictionary<string, IReadOnlyList<BraveSearchResult>>? fixtures = null)
    {
        this.fixtures = fixtures is null
            ? new Dictionary<string, IReadOnlyList<BraveSearchResult>>(
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, IReadOnlyList<BraveSearchResult>>(
                fixtures,
                StringComparer.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<BraveSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(query, NoWebsiteCompanyName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<BraveSearchResult>>([]);
        }

        if (fixtures.TryGetValue(query, out var fixture))
        {
            return Task.FromResult<IReadOnlyList<BraveSearchResult>>(
                fixture.Take(3).ToArray());
        }

        IReadOnlyList<BraveSearchResult> results =
        [
            new(
                new Uri("https://example.test/company"),
                query,
                "Made-up result returned by the fake Brave client."),
        ];

        return Task.FromResult(results);
    }
}
