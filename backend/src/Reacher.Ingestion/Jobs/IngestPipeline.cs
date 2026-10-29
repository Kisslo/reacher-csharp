using Microsoft.EntityFrameworkCore;
using Reacher.Data;
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
/// T1-05, then T1-06 for the companies the search shows. Scoped: both services and this class
/// share one <see cref="ReacherDbContext"/> per job.
/// </summary>
public sealed class IngestPipeline(
    ScbIngestService scb,
    FinancialsIngestService financials,
    ReacherDbContext db) : IIngestPipeline
{
    public async Task RunAsync(
        string sni,
        IReadOnlyCollection<string> municipalities,
        IProgress<IngestProgress> progress,
        CancellationToken cancellationToken)
    {
        await scb.RunAsync(sni, municipalities, progress, cancellationToken);

        // Only the companies touched by this job: after the SCB ingest, the ones with a
        // workplace for this SNI in these municipalities. Never every company in the database.
        var wanted = municipalities.ToList();
        var orgnrs = await db.Workplaces
            .Where(w => w.Sni == sni && wanted.Contains(w.Municipality))
            .Select(w => w.Orgnr)
            .Distinct()
            .ToListAsync(cancellationToken);

        await financials.RunAsync(orgnrs, progress, cancellationToken);
    }
}
