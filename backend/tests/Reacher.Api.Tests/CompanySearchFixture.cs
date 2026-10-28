using Microsoft.EntityFrameworkCore;
using Reacher.Data;
using Reacher.Data.Entities;
using Testcontainers.PostgreSql;

namespace Reacher.Api.Tests;

public sealed class CompanySearchFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    private ReacherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ReacherDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    private static async Task SeedAsync(ReacherDbContext db)
    {
        var companies = new[]
        {
            NewCompany("5599001001", "Alpha AB", "2", "2", new DateOnly(2020, 1, 1)),
            NewCompany("5599001019", "Beta AB", "3", "3", new DateOnly(2021, 1, 1)),
            NewCompany("5599001027", "Gamma AB", "1", "1", null),
            NewCompany("5599001035", "Delta AB", "4", "4", new DateOnly(2019, 1, 1)),
            NewCompany("5599001043", "Other SNI AB", "2", "2", new DateOnly(2018, 1, 1))
        };
        db.Companies.AddRange(companies);
        db.Workplaces.AddRange(
            NewWorkplace("90000001", "5599001001", "0180", "96210"),
            NewWorkplace("90000002", "5599001001", "0184", "96210"),
            NewWorkplace("90000003", "5599001001", "1480", "96220"),
            NewWorkplace("90000004", "5599001019", "0180", "96210"),
            NewWorkplace("90000005", "5599001027", "0184", "96210"),
            NewWorkplace("90000006", "5599001035", "1480", "96210"),
            NewWorkplace("90000007", "5599001043", "0180", "96220"));
        db.FinancialFacts.AddRange(
            NewFact("5599001001", 2024, "revenue", 900_000),
            NewFact("5599001001", 2024, "net_result", 90_000),
            NewFact("5599001001", 2025, "revenue", 1_000_000),
            NewFact("5599001001", 2025, "net_result", 100_000),
            NewFact("5599001019", 2025, "revenue", 500_000),
            NewFact("5599001027", 2025, "net_result", -10_000));
        db.Coverage.AddRange(
            NewCoverage("96210", "0180", DateTimeOffset.UtcNow.AddDays(-2)),
            NewCoverage("96210", "0184", DateTimeOffset.UtcNow.AddDays(-40)));
        await db.SaveChangesAsync();
    }

    private static Company NewCompany(
        string orgnr,
        string name,
        string employeeClass,
        string revenueClass,
        DateOnly? registeredAt) =>
        new()
        {
            Orgnr = orgnr,
            Name = name,
            Sni = "96210",
            LegalForm = "49",
            CompanyStatus = "1",
            EmployeeClass = employeeClass,
            RevenueClass = revenueClass,
            RevenueClassYear = 2025,
            RegisteredAt = registeredAt,
            AdBlockType = "1",
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static Workplace NewWorkplace(
        string cfar,
        string orgnr,
        string municipality,
        string sni) =>
        new()
        {
            Cfar = cfar,
            Orgnr = orgnr,
            Name = null,
            Sni = sni,
            Municipality = municipality,
            Street = "Testgatan 1",
            PostalCode = "111 20",
            City = "Teststad",
            WorkplaceStatus = "1",
            AdBlockType = "1",
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static FinancialFact NewFact(
        string orgnr,
        int year,
        string key,
        decimal value) =>
        new()
        {
            Orgnr = orgnr,
            PeriodEnd = new DateOnly(year, 12, 31),
            Key = key,
            Value = value,
            DocumentId = $"fictional-{orgnr}-{year}",
            FetchedAt = DateTimeOffset.UtcNow
        };

    private static Coverage NewCoverage(
        string sni,
        string municipality,
        DateTimeOffset fetchedAt) =>
        new()
        {
            Sni = sni,
            Municipality = municipality,
            FetchedAt = fetchedAt
        };
}
