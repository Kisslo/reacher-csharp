using System.ComponentModel.DataAnnotations;

namespace Reacher.WebsiteAnalysis.Options;

/// <summary>Brave Search settings, bound from the <c>Brave</c> section.</summary>
public sealed class BraveOptions
{
    public const string SectionName = "Brave";

    [Required(ErrorMessage = "Missing setting 'Brave:ApiKey'.")]
    public string? ApiKey { get; set; }

    public int MonthlyCap { get; set; } = 1500;

    public int RequestsPerSecond { get; set; } = 1;

    public bool UseFake { get; set; }
}
