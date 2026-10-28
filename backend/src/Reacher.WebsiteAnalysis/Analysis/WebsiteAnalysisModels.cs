namespace Reacher.WebsiteAnalysis.Analysis;

public sealed record LighthouseScores(
    int Performance,
    int Accessibility,
    int BestPractices,
    int Seo);

public sealed record WebsiteChecks(
    bool Https,
    bool Viewport,
    string? Generator,
    int? CopyrightYear,
    long ResponseTimeMs);

public sealed record PageFetch(Uri FinalUrl, WebsiteChecks Checks);

/// <summary>
/// Returned to the caller of this request only. Nothing here is written to the
/// database, a cache, a file, or a log (D9, D16).
/// </summary>
public sealed record WebsiteAnalysisResult(
    Uri FinalUrl,
    DateTimeOffset FetchedAt,
    int ReworkScore,
    bool ScoreFromChecksOnly,
    LighthouseScores? Lighthouse,
    WebsiteChecks Checks);

public readonly record struct ReworkScore(int Value, bool ScoreFromChecksOnly);

public readonly record struct HtmlFindings(
    bool Https,
    bool Viewport,
    string? Generator,
    int? CopyrightYear);
