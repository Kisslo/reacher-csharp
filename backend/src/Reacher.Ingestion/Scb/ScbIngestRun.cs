namespace Reacher.Ingestion.Scb;

/// <summary>
/// What one SCB ingest has saved so far. <see cref="ScbIngestService.RunAsync"/> fills it, and
/// <see cref="ScbIngestService.CompleteAsync"/> uses it once the whole job has succeeded (D19).
/// </summary>
/// <remarks>
/// A class, not a record: a record's <c>ToString()</c> would print every orgnr if this ever
/// ended up in a log line (hard rule 6). Log <see cref="Result"/> instead, which holds counts.
/// </remarks>
public sealed class ScbIngestRun
{
    internal ScbIngestRun(string sni, IEnumerable<string> municipalities, DateTimeOffset startedAt)
    {
        Sni = sni;
        Municipalities = municipalities.ToHashSet(StringComparer.Ordinal);
        StartedAt = startedAt;
    }

    public string Sni { get; }

    public IReadOnlySet<string> Municipalities { get; }

    /// <summary>
    /// <c>updated_at</c> on every row, and <c>coverage.fetched_at</c> at the end.
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    public ScbIngestResult Result { get; } = new();

    /// <summary>The companies this run has committed. The financials ingest uses these.</summary>
    public IReadOnlyCollection<string> StoredOrgnrs => Orgnrs;

    internal HashSet<string> Orgnrs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The workplaces this run has committed. Every other one in the requested municipalities
    /// is deleted when the run completes.
    /// </summary>
    internal HashSet<string> Cfars { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Workplaces SCB lists in the requested municipalities but marks as not eligible (D5).
    /// They are deleted as soon as SCB's list has been read, not at the end (D19).
    /// </summary>
    internal HashSet<string> IneligibleCfars { get; } = new(StringComparer.Ordinal);
}
