namespace Reacher.Ingestion.Jobs;

/// <summary>
/// Keeps only the newest progress report. The ingest services report many times a second,
/// in the middle of their own database work, so the worker writes the newest value to
/// <c>ingest_job</c> about once a second instead of on every report.
/// </summary>
internal sealed class LatestProgress : IProgress<IngestProgress>
{
    private IngestProgress? _latest;

    // Report runs on the pipeline's thread and Take on the timer's, so both are atomic.
    public void Report(IngestProgress value) => Volatile.Write(ref _latest, value);

    /// <summary>The newest report since the last call, or null if nothing new came in.</summary>
    public IngestProgress? Take() => Interlocked.Exchange(ref _latest, null);
}
