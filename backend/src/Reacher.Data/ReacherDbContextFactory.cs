using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Reacher.Data;

/// <summary>
/// Used only by <c>dotnet ef</c>. Builds the context without starting the API, so
/// creating or applying a migration needs no API keys.
/// </summary>
public sealed class ReacherDbContextFactory : IDesignTimeDbContextFactory<ReacherDbContext>
{
    // The local Compose database, same as appsettings.Development.json. Not a secret:
    // Postgres only listens on 127.0.0.1.
    private const string LocalDatabase =
        "Host=localhost;Port=5432;Database=reacher;Username=reacher;Password=reacher";

    public ReacherDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? LocalDatabase;
        var options = new DbContextOptionsBuilder<ReacherDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ReacherDbContext(options);
    }
}
