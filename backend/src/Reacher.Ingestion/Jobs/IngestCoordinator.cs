using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Reacher.Data;
using Reacher.Data.Entities;
using Reacher.Ingestion.Options;

namespace Reacher.Ingestion.Jobs;

/// <summary>
/// Turns a search into "fresh", an existing job or a new one (D6), and cancels jobs.
/// Scoped, like the <see cref="ReacherDbContext"/> it uses.
/// </summary>
public sealed class IngestCoordinator(
    ReacherDbContext db,
    IIngestQueue queue,
    IOptions<IngestOptions> options,
    TimeProvider time,
    IngestJobCancellation cancellations) : IIngestCoordinator
{
    // One request at a time in this process, so a double click can't create two jobs for the
    // same search. Static, because a scoped service gets a new instance per HTTP request.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IngestRequestResult> RequestAsync(
        string sni,
        IReadOnlyCollection<string> municipalities,
        bool refresh,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sni);

        // Sorted and without repeats, so the same search always gives the same job row.
        var wanted = municipalities
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (wanted.Count == 0)
        {
            throw new ArgumentException(
                "At least one municipality is required.",
                nameof(municipalities));
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && await IsFreshAsync(sni, wanted, cancellationToken))
            {
                return IngestRequestResult.FreshData;
            }

            if (await FindCoveringJobAsync(sni, wanted, cancellationToken) is { } existing)
            {
                return new IngestRequestResult(existing.Id, existing.Status);
            }

            var job = new IngestJob
            {
                Id = Guid.NewGuid(),
                Sni = sni,
                Municipalities = wanted,
                Status = IngestJobStatus.Queued,
                CreatedAt = time.GetUtcNow(),
            };
            db.IngestJobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);

            // After the save: the worker reads the row, so it must exist when the id arrives.
            await queue.EnqueueAsync(job.Id, cancellationToken);
            return new IngestRequestResult(job.Id, job.Status);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<CancelJobResult> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // The same gate as RequestAsync, so a search can't join a job that is being cancelled.
        await Gate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset? finishedAt = time.GetUtcNow();

            // One UPDATE ... WHERE status IN ('queued', 'running'). The worker moves a job on
            // the same way, so whichever of them comes second changes nothing.
            var changed = await db.IngestJobs
                .Where(j => j.Id == jobId
                    && (j.Status == IngestJobStatus.Queued
                        || j.Status == IngestJobStatus.Running))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(j => j.Status, IngestJobStatus.Cancelled)
                        .SetProperty(j => j.FinishedAt, finishedAt),
                    cancellationToken);

            if (changed == 1)
            {
                // After the update: the worker checks the row before it starts a job, and the
                // token stops a job it has already started.
                cancellations.Cancel(jobId);
                return CancelJobResult.Cancelled;
            }

            var status = await db.IngestJobs
                .Where(j => j.Id == jobId)
                .Select(j => j.Status)
                .FirstOrDefaultAsync(cancellationToken);
            return status switch
            {
                null => CancelJobResult.NotFound,
                IngestJobStatus.Cancelled => CancelJobResult.Cancelled,
                _ => CancelJobResult.NotCancellable,
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// True if every municipality has coverage newer than the cutoff. A municipality with no
    /// coverage row has never been ingested, so it isn't fresh.
    /// </summary>
    private async Task<bool> IsFreshAsync(
        string sni,
        List<string> wanted,
        CancellationToken cancellationToken)
    {
        // The same cutoff as the stale flag in GET /api/companies.
        var cutoff = time.GetUtcNow().AddDays(-options.Value.FreshnessDays);

        // (sni, municipality) is the primary key, so each municipality counts at most once.
        var fresh = await db.Coverage
            .Where(c => c.Sni == sni && wanted.Contains(c.Municipality) && c.FetchedAt >= cutoff)
            .CountAsync(cancellationToken);
        return fresh == wanted.Count;
    }

    /// <summary>A queued or running job for the same SNI and at least these municipalities.</summary>
    private async Task<IngestJob?> FindCoveringJobAsync(
        string sni,
        List<string> wanted,
        CancellationToken cancellationToken)
    {
        var active = await db.IngestJobs
            .Where(j => j.Sni == sni
                && (j.Status == IngestJobStatus.Queued || j.Status == IngestJobStatus.Running))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        // Only a handful of rows (one worker, and jobs are reused), so the superset check is
        // done here in C# rather than in SQL.
        return active.FirstOrDefault(job => wanted.All(job.Municipalities.Contains));
    }
}
