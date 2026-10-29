using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Reacher.Api.Searches;
using Reacher.Ingestion.Jobs;
using Reacher.Ingestion.Scb;

namespace Reacher.Api.Tests;

public sealed class SearchEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Fake_job_reaches_succeeded_and_the_next_search_is_fresh()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));
        using var factory = CreateFakeHost(clock);
        using var client = factory.CreateClient();

        var started = await PostSearch(client, "96210", ["0180"], refresh: false);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var startedBody = await started.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(startedBody);
        Assert.Equal("queued", startedBody["status"]!.GetValue<string>());
        var jobId = startedBody["jobId"]!.GetValue<Guid>();

        var queued = await client.GetAsync(new Uri($"/api/jobs/{jobId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
        var queuedBody = await queued.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(queuedBody);
        Assert.Equal(jobId, queuedBody["jobId"]!.GetValue<Guid>());
        Assert.Equal("queued", queuedBody["status"]!.GetValue<string>());
        Assert.Equal(0, queuedBody["done"]!.GetValue<int>());
        Assert.True(queuedBody.ContainsKey("total"));
        Assert.Null(queuedBody["total"]);
        Assert.True(queuedBody.ContainsKey("phase"));
        Assert.Null(queuedBody["phase"]);

        clock.UtcNow = clock.UtcNow.AddSeconds(5);

        var done = await client.GetAsync(new Uri($"/api/jobs/{jobId}", UriKind.Relative));
        var doneBody = await done.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(doneBody);
        Assert.Equal("succeeded", doneBody["status"]!.GetValue<string>());
        Assert.Equal("financials", doneBody["phase"]!.GetValue<string>());
        Assert.Equal(1, doneBody["done"]!.GetValue<int>());
        Assert.Equal(1, doneBody["total"]!.GetValue<int>());

        var fresh = await PostSearch(client, "96210", ["0180"], refresh: false);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        var freshBody = await fresh.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(freshBody);
        Assert.Equal("fresh", freshBody["status"]!.GetValue<string>());
        Assert.True(freshBody.ContainsKey("jobId"));
        Assert.Null(freshBody["jobId"]);

        var forced = await PostSearch(client, "96210", ["0180"], refresh: true);
        Assert.Equal(HttpStatusCode.Accepted, forced.StatusCode);
        var forcedBody = await forced.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(forcedBody);
        Assert.NotEqual(jobId, forcedBody["jobId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Unknown_job_returns_404()
    {
        using var factory = CreateFakeHost();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri($"/api/jobs/{Guid.NewGuid()}", UriKind.Relative));

        var body = await ReadProblem(response, HttpStatusCode.NotFound);
        Assert.Equal("job_not_found", body["code"]!.GetValue<string>());
        Assert.Equal(404, body["status"]!.GetValue<int>());
    }

    [Fact]
    public async Task Missing_fields_and_bad_shapes_return_400()
    {
        using var factory = CreateFakeHost();
        using var client = factory.CreateClient();

        var empty = await client.PostAsJsonAsync("/api/searches", new { }, Json);
        var emptyBody = await ReadProblem(empty, HttpStatusCode.BadRequest);
        Assert.Equal("invalid_search", emptyBody["code"]!.GetValue<string>());
        var emptyFields = Fields(emptyBody);
        Assert.Contains("sni", emptyFields);
        Assert.Contains("municipalities", emptyFields);

        var shortSni = await PostSearch(client, "9621", ["0180"], refresh: false);
        var shortSniBody = await ReadProblem(shortSni, HttpStatusCode.BadRequest);
        var shortSniError = Assert.Single(shortSniBody["errors"]!.AsArray());
        Assert.Equal("sni", shortSniError!["field"]!.GetValue<string>());
        Assert.Contains(
            "5-digit",
            shortSniError["message"]!.GetValue<string>(),
            StringComparison.Ordinal);

        var shortMunicipality = await PostSearch(client, "96210", ["180"], refresh: false);
        var shortMunicipalityBody = await ReadProblem(shortMunicipality, HttpStatusCode.BadRequest);
        var municipalityError = Assert.Single(shortMunicipalityBody["errors"]!.AsArray());
        Assert.Equal("municipalities", municipalityError!["field"]!.GetValue<string>());
        Assert.Contains(
            "4 digits",
            municipalityError["message"]!.GetValue<string>(),
            StringComparison.Ordinal);

        var tooMany = Enumerable
            .Range(1, 21)
            .Select(i => i.ToString("D4", CultureInfo.InvariantCulture))
            .ToArray();
        var tooManyResponse = await PostSearch(client, "96210", tooMany, refresh: false);
        var tooManyBody = await ReadProblem(tooManyResponse, HttpStatusCode.BadRequest);
        var tooManyError = Assert.Single(tooManyBody["errors"]!.AsArray());
        Assert.Contains(
            "20",
            tooManyError!["message"]!.GetValue<string>(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_codes_return_400()
    {
        using var factory = CreateFakeHost();
        using var client = factory.CreateClient();

        var response = await PostSearch(client, "99999", ["9999"], refresh: false);

        var body = await ReadProblem(response, HttpStatusCode.BadRequest);
        Assert.Equal("invalid_search", body["code"]!.GetValue<string>());
        var fields = Fields(body);
        Assert.Contains("sni", fields);
        Assert.Contains("municipalities", fields);
    }

    [Fact]
    public async Task Scb_failure_returns_502_without_the_upstream_message()
    {
        using var factory = CreateFakeHost(codes: new UnavailableCodeLookup());
        using var client = factory.CreateClient();

        var response = await PostSearch(client, "96210", ["0180"], refresh: false);

        var body = await ReadProblem(response, HttpStatusCode.BadGateway);
        Assert.Equal("scb_unavailable", body["code"]!.GetValue<string>());
        Assert.Equal("SCB is unavailable.", body["title"]!.GetValue<string>());
        Assert.DoesNotContain("kodtabeller", body.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Openapi_contains_search_and_job()
    {
        using var factory = CreateFakeHost();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(body);
        Assert.NotNull(body["paths"]?["/api/searches"]?["post"]);
        Assert.NotNull(body["paths"]?["/api/jobs/{jobId}"]?["get"]);
    }

    [Fact]
    public void Real_coordinator_is_used_unless_the_flag_is_set()
    {
        using var factory = ReacherApiFactory
            .Create(ReacherApiFactory.UnreachableDatabase)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(
                SearchTestServices.RemoveIngestWorker));

        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        Assert.IsType<IngestCoordinator>(provider.GetRequiredService<IIngestCoordinator>());
        Assert.IsType<IngestJobReader>(provider.GetRequiredService<IIngestJobReader>());
        Assert.IsType<ScbCodeLookup>(provider.GetRequiredService<ICodeLookup>());
    }

    [Fact]
    public void Fake_coordinator_is_used_when_the_flag_is_set()
    {
        using var factory = CreateFakeHost();

        using var scope = factory.Services.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IIngestCoordinator>();
        Assert.IsType<FakeIngestCoordinator>(coordinator);
        Assert.Same(
            coordinator,
            scope.ServiceProvider.GetRequiredService<IIngestJobReader>());
        Assert.Empty(
            scope.ServiceProvider.GetServices<IHostedService>().OfType<IngestWorker>());
    }

    private static WebApplicationFactory<Program> CreateFakeHost(
        TimeProvider? clock = null,
        ICodeLookup? codes = null)
    {
        var lookup = codes ?? TestCodeLookup.Instance;
        return ReacherApiFactory.Create(ReacherApiFactory.UnreachableDatabase)
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Ingest:UseFakeCoordinator", "true");
                builder.ConfigureTestServices(services =>
                {
                    if (clock is not null)
                    {
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton(clock);
                    }

                    services.RemoveAll<ICodeLookup>();
                    services.AddSingleton(lookup);
                });
            });
    }

    private static Task<HttpResponseMessage> PostSearch(
        HttpClient client,
        string sni,
        string[] municipalities,
        bool refresh) =>
        client.PostAsJsonAsync(
            "/api/searches",
            new SearchRequest(sni, [.. municipalities], refresh),
            Json);

    private static async Task<JsonObject> ReadProblem(
        HttpResponseMessage response,
        HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.NotNull(body);
        return body;
    }

    private static List<string> Fields(JsonObject body) =>
        body["errors"]!.AsArray().Select(error => error!["field"]!.GetValue<string>()).ToList();

    private sealed class UnavailableCodeLookup : ICodeLookup
    {
        public Task<bool> IsKnownSniAsync(string sni, CancellationToken cancellationToken) =>
            throw new ScbException("GET /kodtabeller/{tabell}: HTTP 503.");

        public Task<IReadOnlyList<string>> UnknownMunicipalitiesAsync(
            IReadOnlyCollection<string> municipalities,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
