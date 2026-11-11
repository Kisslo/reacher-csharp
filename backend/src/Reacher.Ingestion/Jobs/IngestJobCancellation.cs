using System.Collections.Concurrent;

namespace Reacher.Ingestion.Jobs;

/// <summary>
/// The cancellation token of the job the worker is about to run or is running, by job id. A
/// singleton: <see cref="IngestWorker"/> adds and removes the token, and
/// <see cref="IngestCoordinator"/> cancels it. In memory like <see cref="ChannelIngestQueue"/>;
/// the <c>ingest_job</c> row is still the source of truth.
/// </summary>
public sealed class IngestJobCancellation
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobs = new();

    public void Add(Guid jobId, CancellationTokenSource source) => _jobs[jobId] = source;

    public void Remove(Guid jobId) => _jobs.TryRemove(jobId, out _);

    /// <summary>Cancels the job if the worker has it. Does nothing for any other job.</summary>
    public void Cancel(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var source))
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job finished and its token was disposed between the lookup and here.
        }
    }
}
