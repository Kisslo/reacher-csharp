namespace Reacher.Ingestion.Jobs;

/// <summary>
/// Decides whether a search needs an ingest (D6). T2-02's <c>POST /api/searches</c> calls it.
/// </summary>
public interface IIngestCoordinator
{
    /// <summary>
    /// "fresh" with no job if every municipality was ingested within
    /// <c>Ingest:FreshnessDays</c> and <paramref name="refresh"/> is false. Otherwise a queued
    /// or running job that already covers the search, or a new queued job.
    /// </summary>
    Task<IngestRequestResult> RequestAsync(
        string sni,
        IReadOnlyCollection<string> municipalities,
        bool refresh,
        CancellationToken cancellationToken);
}

/// <summary>
/// What <see cref="IIngestCoordinator.RequestAsync"/> decided, in the shape of the
/// <c>POST /api/searches</c> response: <see cref="JobId"/> is null only when the data is fresh.
/// </summary>
/// <param name="Status"><see cref="Fresh"/>, or the job's <c>IngestJobStatus</c>.</param>
public sealed record IngestRequestResult(Guid? JobId, string Status)
{
    public const string Fresh = "fresh";

    public static IngestRequestResult FreshData { get; } = new(null, Fresh);
}
