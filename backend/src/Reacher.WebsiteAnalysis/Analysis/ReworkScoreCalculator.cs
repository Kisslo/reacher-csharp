using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Rework score from 0 to 100. Higher means the page needs more rework.
/// Weights come from <see cref="ReworkScoreOptions"/>.
/// </summary>
/// <remarks>
/// Assumption: when Lighthouse is missing, the score is only the own-check
/// penalties (0–40) and <see cref="ReworkScore.ScoreFromChecksOnly"/> is true.
/// A missing PageSpeed run is not treated as a Lighthouse score of 0, which
/// would mark every outage as needing rework. With Lighthouse present the score is
/// 100 minus the weighted category scores, plus the same penalties, then clamped.
/// Weights are rounded to 4 decimal places so a binary fraction such as 0.4 does
/// not move the result.
/// </remarks>
public sealed class ReworkScoreCalculator
{
    private readonly ReworkScoreOptions _options;

    private readonly TimeProvider _time;

    public ReworkScoreCalculator(IOptions<ReworkScoreOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public ReworkScore Calculate(LighthouseScores? lighthouse, WebsiteChecks checks)
    {
        var penalties = 0;
        if (!checks.Https)
        {
            penalties += _options.MissingHttpsPenalty;
        }

        if (!checks.Viewport)
        {
            penalties += _options.MissingViewportPenalty;
        }

        if (IsStale(checks.CopyrightYear))
        {
            penalties += _options.StaleCopyrightPenalty;
        }

        if (lighthouse is null)
        {
            return new ReworkScore(Clamp(penalties), true);
        }

        var weighted =
            (lighthouse.Performance * Weight(_options.PerformanceWeight))
            + (lighthouse.Accessibility * Weight(_options.AccessibilityWeight))
            + (lighthouse.BestPractices * Weight(_options.BestPracticesWeight))
            + (lighthouse.Seo * Weight(_options.SeoWeight));
        return new ReworkScore(Clamp(100m - weighted + penalties), false);
    }

    private bool IsStale(int? copyrightYear)
    {
        if (copyrightYear is not int year)
        {
            return false;
        }

        var currentYear = StockholmClock.Year(_time);
        return year <= currentYear - _options.StaleCopyrightYears;
    }

    private static decimal Weight(double value) =>
        decimal.Round((decimal)value, 4, MidpointRounding.AwayFromZero);

    private static int Clamp(decimal score)
    {
        var rounded = decimal.Round(score, 0, MidpointRounding.AwayFromZero);
        if (rounded < 0)
        {
            return 0;
        }

        if (rounded > 100)
        {
            return 100;
        }

        return (int)rounded;
    }
}
