using System.Net;

namespace Reacher.Api.Tests;

/// <summary>Tests that need no database, so they run without Docker.</summary>
public sealed class NoDatabaseTests
{
    [Fact]
    public async Task Root_and_deep_links_return_the_frontend()
    {
        var webRoot = CreateWebRoot();
        try
        {
            using var factory = ReacherApiFactory.Create(
                ReacherApiFactory.UnreachableDatabase,
                webRoot);
            using var client = factory.CreateClient();

            using var rootResponse = await client.GetAsync("/");
            using var deepLinkResponse = await client.GetAsync("/companies");

            Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, deepLinkResponse.StatusCode);
            Assert.Equal("text/html", rootResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal("text/html", deepLinkResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal("<!doctype html>", await rootResponse.Content.ReadAsStringAsync());
            Assert.Equal("<!doctype html>", await deepLinkResponse.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Unknown_api_path_returns_not_found_instead_of_the_frontend()
    {
        var webRoot = CreateWebRoot();
        try
        {
            using var factory = ReacherApiFactory.Create(
                ReacherApiFactory.UnreachableDatabase,
                webRoot);
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/api/nope");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual(
                "<!doctype html>",
                await response.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

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

    private static string CreateWebRoot()
    {
        var webRoot = Directory.CreateTempSubdirectory("reacher-webroot-").FullName;
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(webRoot, "app.js"), "console.log('test');");
        return webRoot;
    }
}
