namespace Reacher.Data.Entities;

/// <summary>
/// An eligible workplace (arbetsställe) of a company. The address is the visiting
/// address, and the municipality is where the workplace is (D3).
/// </summary>
public sealed class Workplace
{
    public required string Cfar { get; set; }

    public required string Orgnr { get; set; }

    /// <summary>SCB's <c>ben</c>. Blank for most workplaces.</summary>
    public string? Name { get; set; }

    public required string Sni { get; set; }

    public required string Municipality { get; set; }

    public required string Street { get; set; }

    public required string PostalCode { get; set; }

    public required string City { get; set; }

    public required string WorkplaceStatus { get; set; }

    public required string AdBlockType { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public Company Company { get; set; } = null!;
}
