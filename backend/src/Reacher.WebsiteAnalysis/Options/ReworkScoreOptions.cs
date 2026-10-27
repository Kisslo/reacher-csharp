namespace Reacher.WebsiteAnalysis.Options;

/// <summary>
/// Rework-score weights, bound from the <c>ReworkScore</c> section.
/// Starting assumption for T2-05; change the numbers in appsettings.json.
/// </summary>
public sealed class ReworkScoreOptions
{
    public const string SectionName = "ReworkScore";

    public double PerformanceWeight { get; set; } = 0.4;

    public double AccessibilityWeight { get; set; } = 0.2;

    public double BestPracticesWeight { get; set; } = 0.2;

    public double SeoWeight { get; set; } = 0.2;

    public int MissingHttpsPenalty { get; set; } = 15;

    public int MissingViewportPenalty { get; set; } = 15;

    public int StaleCopyrightPenalty { get; set; } = 10;

    public int StaleCopyrightYears { get; set; } = 3;
}
