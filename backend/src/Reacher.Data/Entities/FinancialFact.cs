namespace Reacher.Data.Entities;

/// <summary>
/// One figure from an annual report, e.g. <c>revenue</c> for one fiscal year (D14).
/// A missing row means "unknown", never 0.
/// </summary>
public sealed class FinancialFact
{
    public required string Orgnr { get; set; }

    public required DateOnly PeriodEnd { get; set; }

    /// <summary>A fact name from <c>Bolagsverket:TagMap</c>, e.g. <c>revenue</c>.</summary>
    public required string Key { get; set; }

    /// <summary>Whole kronor (SEK).</summary>
    public required decimal Value { get; set; }

    public required string DocumentId { get; set; }

    public required DateTimeOffset FetchedAt { get; set; }

    public Company Company { get; set; } = null!;
}
