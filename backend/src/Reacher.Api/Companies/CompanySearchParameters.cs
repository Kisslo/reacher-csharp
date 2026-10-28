namespace Reacher.Api.Companies;

/// <summary>Validated and typed parameters for the company query.</summary>
public sealed record CompanySearchParameters(
    string Sni,
    IReadOnlyList<string> Municipalities,
    IReadOnlyList<string> EmployeeClasses,
    IReadOnlyList<string> RevenueClasses,
    DateOnly? RegisteredFrom,
    DateOnly? RegisteredTo,
    decimal? RevenueMin,
    decimal? RevenueMax,
    decimal? NetResultMin,
    decimal? NetResultMax,
    bool? HasFinancials,
    string Sort,
    string Order,
    int Page,
    int PageSize);
