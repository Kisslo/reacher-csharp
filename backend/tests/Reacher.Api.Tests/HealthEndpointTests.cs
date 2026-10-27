using System.Net;

namespace Reacher.Api.Tests;

public sealed class HealthEndpointTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Health_returns_200_when_database_is_reachable()
    {
        using var factory = ReacherApiFactory.Create(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
