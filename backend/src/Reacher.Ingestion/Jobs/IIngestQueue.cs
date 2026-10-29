using System.Diagnostics.CodeAnalysis;

namespace Reacher.Ingestion.Jobs;

/// <summary>
/// Ingest jobs waiting to run, by <c>ingest_job.id</c>. An interface so the worker can move to
/// a process of its own later. The row in <c>ingest_job</c> is the source of truth, the queue
/// only says which row to run next.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The name is fixed in claude.md and T1-07, and it is a queue.")]
public interface IIngestQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken);

    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
}
