namespace Reacher.WebsiteAnalysis.Matching;

/// <summary>How closely a remaining search result matches the company.</summary>
public enum WebsiteConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>
/// One candidate for the caller of a single match. The URL is not stored anywhere else.
/// </summary>
public sealed record WebsiteCandidate(
    Uri Url,
    string? Title,
    WebsiteConfidence Confidence,
    bool Preselected);
