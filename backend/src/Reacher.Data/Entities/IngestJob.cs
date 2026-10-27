namespace Reacher.Data.Entities;

/// <summary>One ingest run for an SNI code and one or more municipalities (D6).</summary>
public sealed class IngestJob
{
    public Guid Id { get; set; }

    public required string Sni { get; set; }

    public required List<string> Municipalities { get; set; }

    /// <summary>One of the <see cref="IngestJobStatus"/> values.</summary>
    public required string Status { get; set; }

    public string? Phase { get; set; }

    public int Done { get; set; }

    /// <summary>Unknown until the job has listed what it will fetch.</summary>
    public int? Total { get; set; }

    public string? Error { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
