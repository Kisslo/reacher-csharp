using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Brave;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Matching;

/// <summary>
/// Filters and scores at most three Brave results for one company.
/// The returned list is the only place those URLs exist. Nothing is logged or stored.
/// </summary>
public sealed class WebsiteMatcher
{
    private readonly MatchingOptions _options;

    public WebsiteMatcher(IOptions<MatchingOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<WebsiteCandidate> Match(
        string companyName,
        IReadOnlyList<BraveSearchResult> results)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentNullException.ThrowIfNull(results);

        var scored = new List<ScoredResult>(Math.Min(results.Count, 3));
        foreach (var result in results.Take(3))
        {
            if (!TryGetHost(result.Url, out var host) || IsBlocked(host))
            {
                continue;
            }

            var label = WebsiteScorer.ExtractLabel(host);
            if (label.Length == 0)
            {
                continue;
            }

            var score = WebsiteScorer.Score(
                companyName,
                label,
                result.Title,
                _options.TitleBonus);
            scored.Add(new ScoredResult(result, score, Confidence(score)));
        }

        var selected = IndexOfBestHigh(scored);
        var candidates = new List<WebsiteCandidate>(scored.Count);
        for (var index = 0; index < scored.Count; index++)
        {
            var item = scored[index];
            candidates.Add(new WebsiteCandidate(
                item.Result.Url,
                item.Result.Title,
                item.Confidence,
                index == selected));
        }

        return candidates;
    }

    private static int? IndexOfBestHigh(List<ScoredResult> scored)
    {
        int? selected = null;
        var best = 0d;
        for (var index = 0; index < scored.Count; index++)
        {
            if (scored[index].Confidence != WebsiteConfidence.High)
            {
                continue;
            }

            if (selected is not null && scored[index].Score <= best)
            {
                continue;
            }

            selected = index;
            best = scored[index].Score;
        }

        return selected;
    }

    private WebsiteConfidence Confidence(double score)
    {
        if (score >= _options.HighConfidenceThreshold)
        {
            return WebsiteConfidence.High;
        }

        if (score >= _options.MediumConfidenceThreshold)
        {
            return WebsiteConfidence.Medium;
        }

        return WebsiteConfidence.Low;
    }

    private bool IsBlocked(string host)
    {
        foreach (var blocked in _options.BlockedDomains)
        {
            if (string.IsNullOrWhiteSpace(blocked))
            {
                continue;
            }

            var domain = blocked.Trim().TrimEnd('.').ToLowerInvariant();
            if (domain.Length == 0)
            {
                continue;
            }

            if (host.Equals(domain, StringComparison.Ordinal)
                || host.EndsWith('.' + domain, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetHost(Uri url, out string host)
    {
        host = string.Empty;
        if (!url.IsAbsoluteUri)
        {
            return false;
        }

        var value = url.IdnHost.TrimEnd('.');
        if (value.Length == 0)
        {
            return false;
        }

        host = value.ToLowerInvariant();
        return true;
    }

    private readonly record struct ScoredResult(
        BraveSearchResult Result,
        double Score,
        WebsiteConfidence Confidence);
}
