namespace Reacher.Data.Entities;

/// <summary>The values of <c>ingest_job.status</c>, as written in docs/contracts.md.</summary>
public static class IngestJobStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    /// <summary>Stopped by a user (J-17). Rows already committed stay.</summary>
    public const string Cancelled = "cancelled";
}
