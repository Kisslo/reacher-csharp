using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Reacher.Data.Tests;

/// <summary>
/// Starts one throwaway Postgres 17 container per test class and applies every
/// migration (hard rule 8: no shared or external database in tests). Needs Docker running.
/// </summary>
public sealed class MigratedPostgresFixture : IAsyncLifetime
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
}
