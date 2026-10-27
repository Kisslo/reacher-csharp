namespace Reacher.Data.Entities;

/// <summary>An eligible aktiebolag (D2, D5). One list row per company (D15).</summary>
public sealed class Company
{
    /// <summary>10 digits, without the <c>16</c> prefix SCB sends.</summary>
    public required string Orgnr { get; set; }

    public required string Name { get; set; }

    public required string Sni { get; set; }

    // SCB codes, stored as raw text exactly as delivered (D12).
    public required string LegalForm { get; set; }

    public required string CompanyStatus { get; set; }

    public required string EmployeeClass { get; set; }

    public required string RevenueClass { get; set; }

    public required int RevenueClassYear { get; set; }

    public DateOnly? RegisteredAt { get; set; }

    public required string AdBlockType { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Workplace> Workplaces { get; } = new List<Workplace>();

    public ICollection<FinancialFact> FinancialFacts { get; } = new List<FinancialFact>();
}
