namespace Reacher.Data.Entities;

/// <summary>When an SNI code was last ingested for a municipality (D6).</summary>
public sealed class Coverage
{
    public required string Sni { get; set; }

    public required string Municipality { get; set; }

    public required DateTimeOffset FetchedAt { get; set; }
}
