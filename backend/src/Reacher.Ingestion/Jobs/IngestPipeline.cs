using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Scb;

namespace Reacher.Ingestion.Jobs;

/// <summary>
/// The work of one ingest job. An interface so the worker tests can run a fake that throws or
/// waits, without SCB or Bolagsverket.
/// </summary>
public interface IIngestPipeline
{
    Task RunAsync(
        string sni,
        IReadOnlyCollection<string> municipalities,
        IProgress<IngestProgress> progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="ScbIngestService"/>, then <see cref="FinancialsIngestService"/> for the
/// companies the search shows, then <see cref="ScbIngestService.CompleteAsync"/>. Scoped:
/// both services share one DbContext per job.
/// </summary>
public sealed class IngestPipeline(
    ScbIngestService scb,
    FinancialsIngestService financials) : IIngestPipeline
{
    public async Task RunAsync(
        string sni,
        IReadOnlyCollection<string> municipalities,
        IProgress<IngestProgress> progress,
        CancellationToken cancellationToken)
    {
        var run = await scb.RunAsync(sni, municipalities, progress, cancellationToken);

        // Only the companies this job stored. Never every company in the database, and never
        // one the cleanup below is about to delete.
        await financials.RunAsync(run.StoredOrgnrs, progress, cancellationToken);

        // Last, so a job that fails or is cancelled before this line keeps its saved companies
        // but is never marked fresh and never deletes anything (D19).
        await scb.CompleteAsync(run, cancellationToken);
    }
}
