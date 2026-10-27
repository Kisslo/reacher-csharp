using System.Net;

namespace Reacher.Api.Tests;

/// <summary>Tests that need no database, so they run without Docker.</summary>
public sealed class NoDatabaseTests
{
    [Fact]
    public async Task Health_returns_503_when_database_is_unreachable()
    {
        using var factory = ReacherApiFactory.Create(ReacherApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_served_in_development()
    {
        using var factory = ReacherApiFactory.Create(ReacherApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
