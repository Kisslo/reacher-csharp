namespace Reacher.Ingestion;

/// <summary>
/// How far an ingest has come. T1-07 copies it to <c>ingest_job.phase</c>, <c>done</c> and
/// <c>total</c>. <see cref="Total"/> is null while it isn't known yet.
/// </summary>
public sealed record IngestProgress(string Phase, int Done, int? Total);

/// <summary>The values of <see cref="IngestProgress.Phase"/>.</summary>
public static class IngestPhases
{
    /// <summary>Reading SCB's list for the SNI code. Done counts pages.</summary>
    public const string Workplaces = "workplaces";

    /// <summary>Fetching each company from SCB. Done is n of m.</summary>
    public const string Companies = "companies";
}
