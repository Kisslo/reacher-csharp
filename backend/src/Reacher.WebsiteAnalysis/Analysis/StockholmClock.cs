namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>Current calendar year in Sweden, for copyright staleness.</summary>
internal static class StockholmClock
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public static int Year(TimeProvider time) =>
        TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).Year;
}
