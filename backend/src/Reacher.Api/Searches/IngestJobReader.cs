using Microsoft.EntityFrameworkCore;
using Reacher.Data;

namespace Reacher.Api.Searches;

/// <summary>
/// The real job status, read from <c>ingest_job</c>.
/// <see cref="Reacher.Ingestion.Jobs.IngestWorker"/> is what updates the row.
/// </summary>
public sealed class IngestJobReader(ReacherDbContext db) : IIngestJobReader
{
    public async Task<JobResponse?> FindAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.IngestJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        return new JobResponse(
            job.Id,
            job.Status,
            job.Phase,
            job.Done,
            job.Total,
            job.Error,
            job.StartedAt,
            job.FinishedAt);
    }
}
