namespace Reacher.WebsiteAnalysis.Brave;

/// <summary>
/// Searches Brave for one company's website.
/// </summary>
public interface IBraveSearchClient
{
    Task<IReadOnlyList<BraveSearchResult>> SearchAsync(
        string companyName,
        CancellationToken cancellationToken);
}
