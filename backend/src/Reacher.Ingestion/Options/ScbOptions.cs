using System.ComponentModel.DataAnnotations;

namespace Reacher.Ingestion.Options;

/// <summary>SCB AFR API settings, bound from the <c>Scb</c> section.</summary>
public sealed class ScbOptions
{
    public const string SectionName = "Scb";

    [Required(ErrorMessage = "Missing setting 'Scb:BaseUrl'.")]
    public string? BaseUrl { get; set; } = "https://apiafr.scb.se/v1";

    [Required(ErrorMessage = "Missing setting 'Scb:ApiKey'.")]
    public string? ApiKey { get; set; }

    [Range(1, 5000, ErrorMessage = "Setting 'Scb:PageSize' must be between 1 and 5000.")]
    public int PageSize { get; set; } = 5000;
}
