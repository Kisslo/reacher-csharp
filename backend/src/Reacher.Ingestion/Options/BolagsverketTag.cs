namespace Reacher.Ingestion.Options;

/// <summary>One iXBRL tag and the financial fact it maps to.</summary>
public sealed class BolagsverketTag
{
    public string? Tag { get; set; }

    public string? Fact { get; set; }
}
