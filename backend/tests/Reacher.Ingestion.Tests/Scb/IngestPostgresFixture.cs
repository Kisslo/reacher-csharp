using Microsoft.EntityFrameworkCore;
using Reacher.Data;
using Testcontainers.PostgreSql;

namespace Reacher.Ingestion.Tests.Scb;

/// <summary>
/// Starts one throwaway Postgres 17 container per test class and applies every migration
/// (hard rule 8: no shared or external database in tests). Needs Docker running.
/// </summary>
public sealed class IngestPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public ReacherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ReacherDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options);

    /// <summary>Empties the tables the ingest writes, so every test starts clean.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE company, workplace, financial_fact, coverage, ingest_job CASCADE");
    }
}
