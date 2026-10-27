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
        string companyName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(companyName, NoWebsiteCompanyName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<BraveSearchResult>>([]);
        }

        if (fixtures.TryGetValue(companyName, out var fixture))
        {
            return Task.FromResult<IReadOnlyList<BraveSearchResult>>(
                fixture.Take(3).ToArray());
        }

        IReadOnlyList<BraveSearchResult> results =
        [
            new(
                new Uri("https://example.test/company"),
                companyName,
                "Made-up result returned by the fake Brave client."),
        ];

        return Task.FromResult(results);
    }
}
