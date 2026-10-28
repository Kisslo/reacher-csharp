namespace Reacher.WebsiteAnalysis.Brave;

/// <summary>
/// A single transient result returned by Brave Search.
/// </summary>
public sealed record BraveSearchResult(
    Uri Url,
    string? Title,
    string? Description);
