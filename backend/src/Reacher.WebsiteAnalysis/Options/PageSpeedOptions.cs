using System.ComponentModel.DataAnnotations;

namespace Reacher.WebsiteAnalysis.Options;

/// <summary>PageSpeed Insights settings, bound from the <c>PageSpeed</c> section.</summary>
public sealed class PageSpeedOptions
{
    public const string SectionName = "PageSpeed";

    [Required(ErrorMessage = "Missing setting 'PageSpeed:ApiKey'.")]
    public string? ApiKey { get; set; }

    [Required(ErrorMessage = "Missing setting 'PageSpeed:Strategy'.")]
    [RegularExpression(
        "^(mobile|desktop)$",
        ErrorMessage = "Setting 'PageSpeed:Strategy' must be 'mobile' or 'desktop'.")]
    public string? Strategy { get; set; } = "mobile";

    public int TimeoutSeconds { get; set; } = 90;
}
