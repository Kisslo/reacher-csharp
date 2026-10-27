using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Reacher.Data.Tests.TestData;

namespace Reacher.Data.Tests;

public sealed class SchemaTests(MigratedPostgresFixture postgres)
    : IClassFixture<MigratedPostgresFixture>
{
    /// <summary>
    /// The schema in docs/contracts.md. A change here is a contract change: update the
    /// contract in the same PR, approved by both teams.
    /// </summary>
    private static readonly Dictionary<string, string[]> ContractColumns = new()
    {
        ["company"] =
        [
            "orgnr", "name", "sni", "legal_form", "company_status", "employee_class",
            "revenue_class", "revenue_class_year", "registered_at", "ad_block_type",
            "updated_at",
        ],
        ["workplace"] =
        [
            "cfar", "orgnr", "name", "sni", "municipality", "street", "postal_code", "city",
            "workplace_status", "ad_block_type", "updated_at",
        ],
        ["financial_fact"] =
            ["orgnr", "period_end", "key", "value", "document_id", "fetched_at"],
        ["coverage"] = ["sni", "municipality", "fetched_at"],
        ["ingest_job"] =
        [
            "id", "sni", "municipalities", "status", "phase", "done", "total", "error",
            "created_at", "started_at", "finished_at",
        ],
        ["brave_usage"] = ["month", "request_count"],
    };

    [Fact]
    public async Task Migrations_create_exactly_the_tables_and_columns_in_the_contract()
    {
        await using var db = postgres.CreateContext();

        var actual = await db.Database.SqlQuery<string>($"""
            SELECT table_name || '.' || column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
            """).ToListAsync();

        var expected = ContractColumns
            .SelectMany(table => table.Value.Select(column => $"{table.Key}.{column}"));
        Assert.Equal(expected.Order(), actual.Order());
    }

    [Fact]
    public async Task Deleting_a_company_removes_its_workplaces_and_financial_facts()
    {
        await using (var db = postgres.CreateContext())
        {
            var company = NewCompany(Orgnr);
            company.Workplaces.Add(NewWorkplace("90000001", Orgnr));
            company.Workplaces.Add(NewWorkplace("90000002", Orgnr));
            company.FinancialFacts.Add(NewFact(Orgnr, 2025, "revenue", 1_200_000));
            company.FinancialFacts.Add(NewFact(Orgnr, 2025, "net_result", -50_000));
            db.Companies.Add(company);
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateContext())
        {
            // ExecuteDeleteAsync sends one DELETE and loads nothing, so only the
            // ON DELETE CASCADE in the database can remove the children.
            await db.Companies.Where(c => c.Orgnr == Orgnr).ExecuteDeleteAsync();
        }

        await using (var db = postgres.CreateContext())
        {
            Assert.False(await db.Workplaces.AnyAsync(w => w.Orgnr == Orgnr));
            Assert.False(await db.FinancialFacts.AnyAsync(f => f.Orgnr == Orgnr));
        }
    }

    [Theory]
    [InlineData("8013019999")] // personnummer shape; month 13, so it belongs to no one
    [InlineData("165599000015")] // 12 digits, as SCB sends peOrgNr
    [InlineData("559900001")] // 9 digits
    public async Task Company_orgnr_must_have_the_shape_of_an_organisationsnummer(string orgnr)
    {
        await using var db = postgres.CreateContext();
        db.Companies.Add(NewCompany(orgnr));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync());

        var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresError.SqlState);
    }
}
