namespace Reacher.Api.Companies;

/// <summary>The paged response returned by <c>GET /api/companies</c>.</summary>
public sealed record CompanySearchResponse(
    IReadOnlyList<CompanyListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CoverageItem> Coverage);

public sealed record CompanyListItem(
    string Orgnr,
    string Name,
    string Sni,
    string EmployeeClass,
    string RevenueClass,
    int RevenueClassYear,
    DateOnly? RegisteredAt,
    IReadOnlyList<WorkplaceListItem> Workplaces,
    IReadOnlyList<FinancialListItem> Financials);

public sealed record WorkplaceListItem(
    string? Name,
    string Municipality,
    string City);

public sealed record FinancialListItem(
    DateOnly PeriodEnd,
    decimal? Revenue,
    decimal? NetResult);

public sealed record CoverageItem(
    string Municipality,
    DateTimeOffset? FetchedAt,
    bool Stale);
