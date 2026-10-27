using System.ComponentModel.DataAnnotations;

namespace Reacher.Ingestion.Options;

/// <summary>Ingest settings, bound from the <c>Ingest</c> section.</summary>
public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    public int FreshnessDays { get; set; } = 30;

    // Empty here on purpose. The binder appends to a collection that already has
    // items, so the allow-list lives only in appsettings.json. An empty list must
    // not mean "allow every legal form" (fail closed).
    [Required(ErrorMessage = "Missing setting 'Ingest:AllowedLegalForms'.")]
    [MinLength(
        1,
        ErrorMessage = "Setting 'Ingest:AllowedLegalForms' must contain at least one legal form.")]
    public List<string> AllowedLegalForms { get; set; } = [];
}
