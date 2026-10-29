using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Reacher.Api.Searches;
using Reacher.Data;
using Reacher.Data.Entities;

namespace Reacher.Api.Tests;

/// <summary>
/// The real coordinator against Postgres. The worker is removed so the test never calls SCB.
/// </summary>
public sealed class SearchEndpointDatabaseTests(CompanySearchFixture postgres)
    : IClassFixture<CompanySearchFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Fresh_coverage_returns_200()
    {
        using var factory = CreateHost();
        using var client = factory.CreateClient();

        var response = await PostSearch(client, "96210", ["0180"], refresh: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(body);
        Assert.Equal("fresh", body["status"]!.GetValue<string>());
        Assert.True(body.ContainsKey("jobId"));
        Assert.Null(body["jobId"]);
    }

    [Fact]
    public async Task Refresh_starts_a_job_even_when_the_data_is_fresh()
    {
        using var factory = CreateHost();
        using var client = factory.CreateClient();

        var response = await PostSearch(client, "96210", ["0180"], refresh: true);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(body);
        Assert.Equal("queued", body["status"]!.GetValue<string>());
        Assert.NotEqual(Guid.Empty, body["jobId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Stale_coverage_returns_202_and_the_job_can_be_read()
    {
        using var factory = CreateHost();
        using var client = factory.CreateClient();

        var first = await PostSearch(client, "96210", ["0184"], refresh: false);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(firstBody);
        var jobId = firstBody["jobId"]!.GetValue<Guid>();
        Assert.Equal("queued", firstBody["status"]!.GetValue<string>());

        var second = await PostSearch(client, "96210", ["0184"], refresh: false);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(secondBody);
        Assert.Equal(jobId, secondBody["jobId"]!.GetValue<Guid>());

        var job = await client.GetAsync(new Uri($"/api/jobs/{jobId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, job.StatusCode);
        var jobBody = await job.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(jobBody);
        Assert.Equal(jobId, jobBody["jobId"]!.GetValue<Guid>());
        Assert.Equal("queued", jobBody["status"]!.GetValue<string>());
        Assert.Equal(0, jobBody["done"]!.GetValue<int>());
        Assert.True(jobBody.ContainsKey("startedAt"));
        Assert.Null(jobBody["startedAt"]);

        await using var db = new ReacherDbContext(
            new DbContextOptionsBuilder<ReacherDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
        var row = await db.IngestJobs.SingleAsync(candidate => candidate.Id == jobId);
        Assert.Equal(IngestJobStatus.Queued, row.Status);
        Assert.Equal("96210", row.Sni);
        Assert.Equal(["0184"], row.Municipalities);
    }

    [Fact]
    public async Task Unknown_job_returns_404()
    {
        using var factory = CreateHost();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri($"/api/jobs/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.Equal("job_not_found", body!["code"]!.GetValue<string>());
    }

    private WebApplicationFactory<Program> CreateHost() =>
        ReacherApiFactory.Create(postgres.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // The worker would call SCB. These tests only check the HTTP contract.
                SearchTestServices.RemoveIngestWorker(services);
                SearchTestServices.UseTestCodes(services);
            }));

    private static Task<HttpResponseMessage> PostSearch(
        HttpClient client,
        string sni,
        string[] municipalities,
        bool refresh) =>
        client.PostAsJsonAsync(
            "/api/searches",
            new SearchRequest(sni, [.. municipalities], refresh),
            Json);
}
