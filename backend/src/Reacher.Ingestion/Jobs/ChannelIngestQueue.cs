using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace Reacher.Ingestion.Jobs;

/// <summary>
/// The in-process queue: a <see cref="Channel{T}"/> of job ids. A singleton, shared by the
/// coordinator (writes) and the worker (reads). It lives in memory, so a restart empties it;
/// <see cref="IngestWorker"/> refills it from <c>ingest_job</c> at startup.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Named after IIngestQueue, which it implements.")]
public sealed class ChannelIngestQueue : IIngestQueue
{
    // Unbounded: an item is one Guid, and the coordinator reuses a job that already covers a
    // search, so the queue stays short. SingleReader, because only the worker reads.
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
