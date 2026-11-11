using System.ComponentModel.DataAnnotations;

namespace Reacher.Ingestion.Options;

/// <summary>Ingest settings, bound from the <c>Ingest</c> section.</summary>
public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    public int FreshnessDays { get; set; } = 30;

    /// <summary>
    /// How many companies the SCB ingest fetches between two commits (D19). Smaller shows
    /// companies in the list sooner. At 2 SCB requests per second, 10 is about 5 seconds.
    /// </summary>
    [Range(1, 1000, ErrorMessage = "Setting 'Ingest:SaveBatchSize' must be between 1 and 1000.")]
    public int SaveBatchSize { get; set; } = 10;

    // Empty here on purpose. The binder appends to a collection that already has
    // items, so the allow-list lives only in appsettings.json. An empty list must
    // not mean "allow every legal form" (fail closed).
    [Required(ErrorMessage = "Missing setting 'Ingest:AllowedLegalForms'.")]
    [MinLength(
        1,
        ErrorMessage = "Setting 'Ingest:AllowedLegalForms' must contain at least one legal form.")]
    public List<string> AllowedLegalForms { get; set; } = [];

    /// <summary>
    /// When true, <c>POST /api/searches</c> uses an in-memory coordinator that simulates
    /// progress. False uses <c>IngestCoordinator</c>.
    /// </summary>
    public bool UseFakeCoordinator { get; set; }
}
