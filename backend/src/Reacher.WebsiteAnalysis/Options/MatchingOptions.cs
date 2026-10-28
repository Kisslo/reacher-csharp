using System.ComponentModel.DataAnnotations;

namespace Reacher.WebsiteAnalysis.Options;

/// <summary>
/// Website-matching settings, bound from the <c>Matching</c> section.
/// Thresholds and the title bonus are an assumption until T2-07 measures them.
/// </summary>
public sealed class MatchingOptions : IValidatableObject
{
    public const string SectionName = "Matching";

    // Empty here on purpose. The binder appends to a collection that already has
    // items, so the block list lives only in appsettings.json.
    [Required(ErrorMessage = "Missing setting 'Matching:BlockedDomains'.")]
    [MinLength(
        1,
        ErrorMessage = "Setting 'Matching:BlockedDomains' must contain at least one domain.")]
    public List<string> BlockedDomains { get; set; } = [];

    [Range(
        0d,
        1d,
        ErrorMessage = "Setting 'Matching:HighConfidenceThreshold' must be between 0 and 1.")]
    public double HighConfidenceThreshold { get; set; } = 0.8;

    [Range(
        0d,
        1d,
        ErrorMessage = "Setting 'Matching:MediumConfidenceThreshold' must be between 0 and 1.")]
    public double MediumConfidenceThreshold { get; set; } = 0.5;

    [Range(
        0d,
        1d,
        ErrorMessage = "Setting 'Matching:TitleBonus' must be between 0 and 1.")]
    public double TitleBonus { get; set; } = 0.15;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HighConfidenceThreshold <= MediumConfidenceThreshold)
        {
            yield return new ValidationResult(
                "Setting 'Matching:HighConfidenceThreshold' must be greater than "
                + "'Matching:MediumConfidenceThreshold'.");
        }

        if (BlockedDomains is null)
        {
            yield break;
        }

        foreach (var domain in BlockedDomains)
        {
            if (string.IsNullOrWhiteSpace(domain)
                || domain.Contains("://", StringComparison.Ordinal)
                || domain.Contains('/', StringComparison.Ordinal)
                || domain.Contains(':', StringComparison.Ordinal))
            {
                yield return new ValidationResult(
                    "Setting 'Matching:BlockedDomains' must contain host names, "
                    + "without a scheme, port, or path.");
                yield break;
            }
        }
    }
}
