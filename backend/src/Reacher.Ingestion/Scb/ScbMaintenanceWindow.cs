namespace Reacher.Ingestion.Scb;

/// <summary>SCB's nightly load, 04:00-04:30 Swedish time.</summary>
public static class ScbMaintenanceWindow
{
    // .NET accepts the IANA id on Windows too, so no extra package is needed.
    private static readonly TimeZoneInfo Stockholm =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public static bool Contains(DateTimeOffset moment)
    {
        var local = TimeZoneInfo.ConvertTime(moment, Stockholm);
        return local.Hour == 4 && local.Minute < 30;
    }
}
