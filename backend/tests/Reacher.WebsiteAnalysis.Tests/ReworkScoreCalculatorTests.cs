using Reacher.WebsiteAnalysis.Analysis;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class ReworkScoreCalculatorTests
{
    private readonly ReworkScoreCalculator _calculator = new(
        OptionsFactory.Create(new ReworkScoreOptions()),
        AnalysisClock.Time);

    private static readonly LighthouseScores Mid = new(40, 50, 60, 70);

    [Fact]
    public void Weighted_scores_use_the_starting_weights()
    {
        var score = _calculator.Calculate(Mid, Checks());

        Assert.Equal(48, score.Value);
        Assert.False(score.ScoreFromChecksOnly);
    }

    [Fact]
    public void Perfect_lighthouse_scores_with_no_penalties_are_zero()
    {
        var score = _calculator.Calculate(new LighthouseScores(100, 100, 100, 100), Checks());

        Assert.Equal(0, score.Value);
    }

    [Fact]
    public void Penalties_are_added_for_each_failed_check()
    {
        Assert.Equal(63, _calculator.Calculate(Mid, Checks(https: false)).Value);
        Assert.Equal(63, _calculator.Calculate(Mid, Checks(viewport: false)).Value);
        Assert.Equal(58, _calculator.Calculate(Mid, Checks(year: 2023)).Value);
        Assert.Equal(48, _calculator.Calculate(Mid, Checks(year: 2024)).Value);
        Assert.Equal(48, _calculator.Calculate(Mid, Checks(year: null)).Value);
    }

    [Fact]
    public void Score_is_clamped_to_0_and_100()
    {
        var worst = _calculator.Calculate(
            new LighthouseScores(0, 0, 0, 0),
            Checks(https: false, viewport: false, year: 2020));
        Assert.Equal(100, worst.Value);

        var heavy = new ReworkScoreCalculator(
            OptionsFactory.Create(new ReworkScoreOptions
            {
                PerformanceWeight = 2,
                AccessibilityWeight = 0,
                BestPracticesWeight = 0,
                SeoWeight = 0,
            }),
            AnalysisClock.Time);
        Assert.Equal(0, heavy.Calculate(new LighthouseScores(100, 0, 0, 0), Checks()).Value);
    }

    [Fact]
    public void A_halfway_score_rounds_away_from_zero()
    {
        var calculator = new ReworkScoreCalculator(
            OptionsFactory.Create(new ReworkScoreOptions
            {
                PerformanceWeight = 1.5,
                AccessibilityWeight = 0,
                BestPracticesWeight = 0,
                SeoWeight = 0,
            }),
            AnalysisClock.Time);
        var score = calculator.Calculate(new LighthouseScores(1, 0, 0, 0), Checks());

        Assert.Equal(99, score.Value);
    }

    [Fact]
    public void Missing_lighthouse_scores_penalties_only()
    {
        var clean = _calculator.Calculate(null, Checks());
        Assert.Equal(0, clean.Value);
        Assert.True(clean.ScoreFromChecksOnly);

        var penalised = _calculator.Calculate(null, Checks(https: false));
        Assert.Equal(15, penalised.Value);
        Assert.True(penalised.ScoreFromChecksOnly);
    }

    private static WebsiteChecks Checks(bool https = true, bool viewport = true, int? year = 2026) =>
        new(https, viewport, null, year, 1);
}
