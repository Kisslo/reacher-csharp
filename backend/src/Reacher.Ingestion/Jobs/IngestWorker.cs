using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Reacher.Data;
using Reacher.Data.Entities;
using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Scb;

namespace Reacher.Ingestion.Jobs;

/// <summary>
/// Runs ingest jobs from <see cref="IIngestQueue"/>, one at a time, so SCB and Bolagsverket
/// only ever see one ingest. Keeps <c>ingest_job</c> up to date:
/// <c>queued</c> → <c>running</c> (progress about once a second) → <c>succeeded</c> or
/// <c>failed</c>.
/// </summary>
/// <remarks>
/// A singleton (hosted service), so each job gets its own DI scope and with it its own
/// <see cref="ReacherDbContext"/>. A failing job never stops the worker.
/// </remarks>
public sealed partial class IngestWorker(
    IIngestQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<IngestWorker> logger) : BackgroundService
{
    public const string InterruptedError = "Interrupted by restart.";

    /// <summary><c>ingest_job.error</c> is shown in the frontend, so it is kept short.</summary>
    private const int MaxErrorLength = 200;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        // One job at a time, in the order they were requested.
        await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
        {
            await RunJobAsync(jobId, stoppingToken);
        }
    }

    /// <summary>
    /// The queue lives in memory, so a restart empties it. A job that was running can't be
    /// resumed and is marked failed. A job that was still queued is put back in the queue.
    /// </summary>
    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReacherDbContext>();

            var interrupted = await db.IngestJobs
                .Where(j => j.Status == IngestJobStatus.Running)
                .ToListAsync(stoppingToken);
            foreach (var job in interrupted)
            {
                job.Status = IngestJobStatus.Failed;
                job.Error = InterruptedError;
                job.FinishedAt = time.GetUtcNow();
            }

            await db.SaveChangesAsync(stoppingToken);

            var queued = await db.IngestJobs
                .Where(j => j.Status == IngestJobStatus.Queued)
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.Id)
                .ToListAsync(stoppingToken);
            foreach (var jobId in queued)
            {
                await queue.EnqueueAsync(jobId, stoppingToken);
            }

            LogRecovered(logger, interrupted.Count, queued.Count);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // A missing database must not stop the API. /health already reports it.
            LogRecoveryFailed(logger, SafeReason(exception));
        }
    }

    private async Task RunJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        // Outside the try, so a failed job also gets its last progress: where it stopped.
        var progress = new LatestProgress();
        try
        {
            if (await MarkRunningAsync(jobId, stoppingToken) is not { } job)
            {
                return;
            }

            using var stopFlushing = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var flushing = FlushProgressAsync(jobId, progress, stopFlushing.Token);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<IIngestPipeline>();
                await pipeline.RunAsync(job.Sni, job.Municipalities, progress, stoppingToken);
            }
            finally
            {
                await stopFlushing.CancelAsync();
                await flushing;
            }

            var last = progress.Take();
            await UpdateAsync(
                jobId,
                row =>
                {
                    Apply(row, last);
                    row.Status = IngestJobStatus.Succeeded;
                    row.FinishedAt = time.GetUtcNow();
                },
                stoppingToken);
            LogSucceeded(logger, jobId);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // This job fails, and the loop goes on with the next one.
            var reason = SafeReason(exception);
            LogFailed(logger, jobId, reason);
            await MarkFailedAsync(jobId, reason, progress.Take(), stoppingToken);
        }

        // On shutdown the job is left as running, and the next start marks it failed.
    }

    /// <summary>The job, now running. Null if it isn't queued: already run, or queued twice.</summary>
    private async Task<IngestJob?> MarkRunningAsync(Guid jobId, CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReacherDbContext>();
        var job = await db.IngestJobs.FindAsync([jobId], stoppingToken);
        if (job is not { Status: IngestJobStatus.Queued })
        {
            return null;
        }

        job.Status = IngestJobStatus.Running;
        job.StartedAt = time.GetUtcNow();
        await db.SaveChangesAsync(stoppingToken);
        return job;
    }

    /// <summary>Writes the newest progress once a second until the job is done.</summary>
    private async Task FlushProgressAsync(
        Guid jobId,
        LatestProgress progress,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ProgressInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (progress.Take() is not { } latest)
                {
                    continue;
                }

                try
                {
                    await UpdateAsync(jobId, row => Apply(row, latest), cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // A missed progress update is not worth failing the job for.
                    LogProgressFailed(logger, jobId, SafeReason(exception));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The job is done, or the API is stopping.
        }
    }

    private async Task MarkFailedAsync(
        Guid jobId,
        string reason,
        IngestProgress? last,
        CancellationToken stoppingToken)
    {
        try
        {
            await UpdateAsync(
                jobId,
                row =>
                {
                    Apply(row, last);
                    row.Status = IngestJobStatus.Failed;
                    row.Error = reason;
                    row.FinishedAt = time.GetUtcNow();
                },
                stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Never let this stop the worker. The next start marks the job failed.
            LogMarkFailedFailed(logger, jobId, SafeReason(exception));
        }
    }

    /// <summary>
    /// Loads the row in a scope of its own, changes it and saves it. The pipeline's
    /// DbContext is busy while the job runs, and a DbContext is not thread-safe.
    /// </summary>
    private async Task UpdateAsync(
        Guid jobId,
        Action<IngestJob> change,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReacherDbContext>();
        var job = await db.IngestJobs.FindAsync([jobId], cancellationToken)
            ?? throw new InvalidOperationException($"Ingest job {jobId} no longer exists.");
        change(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(IngestJob job, IngestProgress? progress)
    {
        if (progress is null)
        {
            return;
        }

        job.Phase = progress.Phase;
        job.Done = progress.Done;
        job.Total = progress.Total;
    }

    /// <summary>
    /// Our own exceptions are written to be safe to log and show: route templates and
    /// document ids, never an orgnr, a URL or a key. Any other message could quote data (a
    /// database error can hold key values), so only its type is kept (hard rule 6).
    /// </summary>
    internal static string SafeReason(Exception exception)
    {
        var reason = exception is ScbException or BolagsverketException
            ? exception.Message
            : $"Unexpected error ({exception.GetType().Name}).";
        return reason.Length <= MaxErrorLength ? reason : reason[..MaxErrorLength];
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Ingest worker started: {Interrupted} interrupted jobs marked failed, "
            + "{Queued} queued jobs put back in the queue")]
    private static partial void LogRecovered(ILogger logger, int interrupted, int queued);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ingest worker could not check for interrupted jobs: {Reason}")]
    private static partial void LogRecoveryFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingest job {JobId} succeeded")]
    private static partial void LogSucceeded(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingest job {JobId} failed: {Reason}")]
    private static partial void LogFailed(ILogger logger, Guid jobId, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Progress for ingest job {JobId} could not be saved: {Reason}")]
    private static partial void LogProgressFailed(ILogger logger, Guid jobId, string reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Ingest job {JobId} could not be marked failed: {Reason}")]
    private static partial void LogMarkFailedFailed(ILogger logger, Guid jobId, string reason);
}
