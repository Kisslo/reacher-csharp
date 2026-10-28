using System.ComponentModel.DataAnnotations;

namespace Reacher.WebsiteAnalysis.Options;

/// <summary>Brave Search settings, bound from the <c>Brave</c> section.</summary>
public sealed class BraveOptions : IValidatableObject
{
    public const string SectionName = "Brave";

    [Required(ErrorMessage = "Missing setting 'Brave:BaseAddress'.")]
    public string BaseAddress { get; set; } = "https://api.search.brave.com/";

    public string? ApiKey { get; set; }

    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Setting 'Brave:MonthlyCap' must be greater than zero.")]
    public int MonthlyCap { get; set; } = 1500;

    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Setting 'Brave:RequestsPerSecond' must be greater than zero.")]
    public int RequestsPerSecond { get; set; } = 1;

    public bool UseFake { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(BaseAddress, UriKind.Absolute, out var baseAddress)
            || baseAddress.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseAddress.UserInfo))
        {
            yield return new ValidationResult(
                "Setting 'Brave:BaseAddress' must be an HTTPS URL without credentials.");
        }

        if (!UseFake && string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return new ValidationResult("Missing setting 'Brave:ApiKey'.");
        }
    }
}
