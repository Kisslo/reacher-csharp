using Reacher.WebsiteAnalysis.Matching;

namespace Reacher.WebsiteAnalysis.Brave;

/// <summary>
/// Searches Brave for one company's website.
/// </summary>
public interface IBraveSearchClient
{
    /// <summary>
    /// Runs one search. <paramref name="query"/> is sent unchanged as <c>q</c>.
    /// Build it with <see cref="WebsiteQuery.Build(string, string?)"/>; this method does not
    /// strip the legal form or add the city.
    /// </summary>
    Task<IReadOnlyList<BraveSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}
