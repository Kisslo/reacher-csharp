namespace Reacher.Api.Searches;

/// <summary>Reads one ingest job for <c>GET /api/jobs/{jobId}</c>. Nothing is written.</summary>
public interface IIngestJobReader
{
    Task<JobResponse?> FindAsync(Guid jobId, CancellationToken cancellationToken);
}
