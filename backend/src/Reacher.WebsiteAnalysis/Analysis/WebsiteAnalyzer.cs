namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Validates a URL, then runs PageSpeed and our own checks together. The result
/// stays in memory for this call. PageSpeed failure still returns the own checks.
/// </summary>
public sealed class WebsiteAnalyzer
{
    private readonly UrlSafety _safety;

    private readonly WebsitePageFetcher _fetcher;

    private readonly PageSpeedClient _pageSpeed;

    private readonly ReworkScoreCalculator _calculator;

    private readonly TimeProvider _time;

    public WebsiteAnalyzer(
        UrlSafety safety,
        WebsitePageFetcher fetcher,
        PageSpeedClient pageSpeed,
        ReworkScoreCalculator calculator,
        TimeProvider time)
    {
        _safety = safety;
        _fetcher = fetcher;
        _pageSpeed = pageSpeed;
        _calculator = calculator;
        _time = time;
    }

    public async Task<WebsiteAnalysisResult> AnalyzeAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var initial = await _safety.ValidateAsync(url, cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var fetchTask = _fetcher.FetchAsync(initial, linked.Token);
        var lighthouseTask = _pageSpeed.GetScoresAsync(initial, linked.Token);

        PageFetch fetched;
        try
        {
            fetched = await fetchTask;
        }
        catch
        {
            await CancelQuietlyAsync(linked, lighthouseTask);
            throw;
        }

        var lighthouse = await lighthouseTask;
        var score = _calculator.Calculate(lighthouse, fetched.Checks);
        return new WebsiteAnalysisResult(
            fetched.FinalUrl,
            _time.GetUtcNow(),
            score.Value,
            score.ScoreFromChecksOnly,
            lighthouse,
            fetched.Checks);
    }

    private static async Task CancelQuietlyAsync(CancellationTokenSource linked, Task other)
    {
        linked.Cancel();
        try
        {
            await other;
        }
        catch (OperationCanceledException)
        {
            // PageSpeed was cancelled because our own fetch failed.
        }
        catch (Exception)
        {
            // PageSpeed failed on its own. The fetch error is the one to report.
        }
    }
}
