using Microsoft.EntityFrameworkCore;

namespace Reacher.Data.Tests;

public sealed class DevSeedTests(MigratedPostgresFixture postgres)
    : IClassFixture<MigratedPostgresFixture>
{
    [Fact]
    public async Task Dev_seed_loads_on_the_migrated_schema_and_can_run_again()
    {
        var seed = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "dev-seed.sql"));
        await using var db = postgres.CreateContext();

        await db.Database.ExecuteSqlRawAsync(seed);
        await db.Database.ExecuteSqlRawAsync(seed);

        Assert.Equal(30, await db.Companies.CountAsync());
        Assert.True(await db.Companies.AllAsync(c => c.Workplaces.Any()));
        Assert.True(await db.Companies.AnyAsync(c => c.FinancialFacts.Any()));
        Assert.True(await db.Companies.AnyAsync(c => !c.FinancialFacts.Any()));
    }
}
