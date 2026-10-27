using Reacher.Data.Entities;

namespace Reacher.Data.Tests;

/// <summary>Made-up rows. The orgnr has a wrong check digit, like the dev seed.</summary>
internal static class TestData
{
    public const string Orgnr = "5599000015";

    public static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    public static Company NewCompany(string orgnr) => new()
    {
        Orgnr = orgnr,
        Name = "Testbolaget AB",
        Sni = "96210",
        LegalForm = "49",
        CompanyStatus = "1",
        EmployeeClass = "2",
        RevenueClass = "2",
        RevenueClassYear = 2025,
        AdBlockType = "1",
        UpdatedAt = Now,
    };

    public static Workplace NewWorkplace(string cfar, string orgnr) => new()
    {
        Cfar = cfar,
        Orgnr = orgnr,
        Sni = "96210",
        Municipality = "0180",
        Street = "Testgatan 1",
        PostalCode = "111 20",
        City = "Stockholm",
        WorkplaceStatus = "1",
        AdBlockType = "1",
        UpdatedAt = Now,
    };

    public static FinancialFact NewFact(string orgnr, int year, string key, decimal value) =>
        new()
        {
            Orgnr = orgnr,
            PeriodEnd = new DateOnly(year, 12, 31),
            Key = key,
            Value = value,
            DocumentId = $"test-{year}",
            FetchedAt = Now,
        };
}
