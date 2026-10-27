using Testcontainers.PostgreSql;

namespace Reacher.Api.Tests;

/// <summary>
/// Starts one throwaway Postgres 17 container per test class (hard rule 8: no shared
/// or external database in tests). Needs Docker running.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
