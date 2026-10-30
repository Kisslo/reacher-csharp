namespace Reacher.Api.Tests;

/// <summary>Known codes for search tests. Keeps <c>POST /api/searches</c> off the real SCB API.</summary>
internal sealed class TestCodeLookup : Reacher.Api.Searches.ICodeLookup
{
    public static TestCodeLookup Instance { get; } = new();

    private static readonly HashSet<string> Sni = ["96210", "96220"];

    private static readonly HashSet<string> Municipalities = ["0180", "0184", "1480"];

    public Task<bool> IsKnownSniAsync(string sni, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Sni.Contains(sni));
    }

    public Task<IReadOnlyList<string>> UnknownMunicipalitiesAsync(
        IReadOnlyCollection<string> municipalities,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            municipalities.Where(code => !Municipalities.Contains(code)).ToList());
    }
}
