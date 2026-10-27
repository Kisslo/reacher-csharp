using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Reacher.Api.Tests;

internal static class ReacherApiFactory
{
    /// <summary>Nothing listens on port 1, so connecting fails at once.</summary>
    public const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    /// <summary>Runs the real Program.cs in memory against the given database.</summary>
    public static WebApplicationFactory<Program> Create(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Default", connectionString));
}
