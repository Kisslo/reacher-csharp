using System.ComponentModel.DataAnnotations;

namespace Reacher.Ingestion.Options;

/// <summary>
/// Bolagsverket annual-report settings, bound from the <c>Bolagsverket</c> section.
/// Tag names contain a colon, which is also the configuration path separator, so
/// <see cref="TagMap"/> is a list rather than a dictionary.
/// </summary>
public sealed class BolagsverketOptions : IValidatableObject
{
    public const string SectionName = "Bolagsverket";

    [Required(ErrorMessage = "Missing setting 'Bolagsverket:TokenUrl'.")]
    public string? TokenUrl { get; set; } = "https://portal.api.bolagsverket.se/oauth2/token";

    [Required(ErrorMessage = "Missing setting 'Bolagsverket:ApiUrl'.")]
    public string? ApiUrl { get; set; } =
        "https://gw.api.bolagsverket.se/vardefulla-datamangder/v1";

    [Required(ErrorMessage = "Missing setting 'Bolagsverket:Scope'.")]
    public string? Scope { get; set; } = "vardefulla-datamangder:read";

    [Required(ErrorMessage = "Missing setting 'Bolagsverket:ClientId'.")]
    public string? ClientId { get; set; }

    [Required(ErrorMessage = "Missing setting 'Bolagsverket:ClientSecret'.")]
    public string? ClientSecret { get; set; }

    public int Years { get; set; } = 3;

    // Empty here on purpose. The binder appends to a collection that already has
    // items, so the real pairs live only in appsettings.json.
    [Required(ErrorMessage = "Missing setting 'Bolagsverket:TagMap'.")]
    [MinLength(1, ErrorMessage = "Setting 'Bolagsverket:TagMap' must contain at least one tag.")]
    public List<BolagsverketTag> TagMap { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TagMap is null)
        {
            yield break;
        }

        for (var i = 0; i < TagMap.Count; i++)
        {
            var entry = TagMap[i];
            if (entry is null
                || string.IsNullOrWhiteSpace(entry.Tag)
                || string.IsNullOrWhiteSpace(entry.Fact))
            {
                yield return new ValidationResult(
                    $"Setting 'Bolagsverket:TagMap' entry {i} needs both Tag and Fact.");
            }
        }
    }
}
