using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reacher.Data;

namespace Reacher.Api.Tests;

public sealed class MigrateOnStartupTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Api_applies_migrations_at_startup_when_enabled()
    {
        using var baseFactory = ReacherApiFactory.Create(postgres.ConnectionString);
        using var factory = baseFactory.WithWebHostBuilder(
            builder => builder.UseSetting("Database:MigrateOnStartup", "true"));

        // Services starts the host, which runs Program.cs.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReacherDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
