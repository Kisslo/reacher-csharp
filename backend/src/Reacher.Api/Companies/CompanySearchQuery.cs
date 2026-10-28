namespace Reacher.Api.Companies;

/// <summary>Raw query-string values for <c>GET /api/companies</c>.</summary>
public sealed class CompanySearchQuery
{
    public string? Sni { get; set; }

    public string[] Municipality { get; set; } = [];

    public string[] EmployeeClass { get; set; } = [];

    public string[] RevenueClass { get; set; } = [];

    public string? RegisteredFrom { get; set; }

    public string? RegisteredTo { get; set; }

    public string? RevenueMin { get; set; }

    public string? RevenueMax { get; set; }

    public string? NetResultMin { get; set; }

    public string? NetResultMax { get; set; }

    public string? HasFinancials { get; set; }

    public string? Sort { get; set; }

    public string? Order { get; set; }

    public string? Page { get; set; }

    public string? PageSize { get; set; }
}
