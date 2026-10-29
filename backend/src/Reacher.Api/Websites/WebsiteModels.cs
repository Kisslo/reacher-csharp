using Reacher.WebsiteAnalysis.Matching;

namespace Reacher.Api.Websites;

public sealed record WebsiteCandidatesResponse(
    IReadOnlyList<WebsiteCandidateResponse> Candidates,
    int RemainingMonthlyQuota);

public sealed record WebsiteCandidateResponse(
    string Url,
    string? Title,
    WebsiteConfidence Confidence,
    bool Preselected);

public sealed record WebsiteAnalysisRequest(string? Url);

/// <summary>
/// Contract fields only. <c>scoreFromChecksOnly</c> stays off the wire: a null
/// <see cref="Lighthouse"/> means the score came from our own checks.
/// </summary>
public sealed record WebsiteAnalysisResponse(
    string Url,
    DateTimeOffset FetchedAt,
    int ReworkScore,
    LighthouseResponse? Lighthouse,
    WebsiteChecksResponse Checks);

public sealed record LighthouseResponse(
    int Performance,
    int Accessibility,
    int BestPractices,
    int Seo);

public sealed record WebsiteChecksResponse(
    bool Https,
    bool Viewport,
    string? Generator,
    int? CopyrightYear,
    long ResponseTimeMs);

public sealed record WebsiteProblem(
    string Type,
    string Title,
    int Status,
    string Code);
