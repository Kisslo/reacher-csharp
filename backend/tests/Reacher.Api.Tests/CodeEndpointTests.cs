using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reacher.Api.Codes;
using Reacher.Ingestion.Scb;

namespace Reacher.Api.Tests;

public sealed class CodeEndpointTests
{
    private const int MunicipalityCount = 290;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task All_290_municipalities_are_returned_sorted_by_name()
    {
        // SCB's order is not the name order, and its table also has two placeholders.
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.MunicipalityTableName] = Enumerable
            .Range(101, MunicipalityCount)
            .Reverse()
            .Select(number => number.ToString("D4", CultureInfo.InvariantCulture))
            .Select(code => new CodeItem(code, "Kommun " + code))
            .Append(new CodeItem("0000", "Kommunkod saknas"))
            .Append(new CodeItem("9999", "Ej svensk hemortskommun"))
            .ToList();
        using var factory = CreateHost(scb);
        using var client = factory.CreateClient();

        var municipalities = await client.GetFromJsonAsync<List<CodeResponse>>(
            new Uri("/api/codes/municipalities", UriKind.Relative),
            Json);

        Assert.NotNull(municipalities);
        Assert.Equal(MunicipalityCount, municipalities.Count);
        Assert.Equal(new CodeResponse("0101", "Kommun 0101"), municipalities[0]);
        Assert.Equal(new CodeResponse("0390", "Kommun 0390"), municipalities[^1]);
    }

    [Fact]
    public async Task Employee_and_revenue_classes_are_returned_as_code_and_name()
    {
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.EmployeeClassTableName] = [new CodeItem("2", "1-4 anställda")];
        scb.Tables[ScbCodeTables.RevenueClassTableName] = [new CodeItem("3", "1 000-4 999 tkr")];
        using var factory = CreateHost(scb);
        using var client = factory.CreateClient();

        var employees = await client.GetFromJsonAsync<JsonArray>(
            new Uri("/api/codes/employee-classes", UriKind.Relative),
            Json);
        var revenue = await client.GetFromJsonAsync<JsonArray>(
            new Uri("/api/codes/revenue-classes", UriKind.Relative),
            Json);

        Assert.NotNull(employees);
        Assert.NotNull(revenue);
        var employee = Assert.Single(employees)!;
        Assert.Equal("2", employee["code"]!.GetValue<string>());
        Assert.Equal("1-4 anställda", employee["name"]!.GetValue<string>());
        var revenueClass = Assert.Single(revenue)!;
        Assert.Equal("3", revenueClass["code"]!.GetValue<string>());
        Assert.Equal("1 000-4 999 tkr", revenueClass["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_known_sni_code_returns_its_name()
    {
        using var factory = CreateHost(WithSni());
        using var client = factory.CreateClient();

        var sni = await client.GetFromJsonAsync<CodeResponse>(
            new Uri("/api/codes/sni/96210", UriKind.Relative),
            Json);

        Assert.Equal(new CodeResponse("96210", "Frisörer och barberare"), sni);
    }

    [Fact]
    public async Task An_unknown_sni_code_returns_404()
    {
        using var factory = CreateHost(WithSni());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/codes/sni/99999", UriKind.Relative));

        var body = await ReadProblem(response, HttpStatusCode.NotFound);
        Assert.Equal("sni_not_found", body["code"]!.GetValue<string>());
        Assert.Equal(404, body["status"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("9621")]
    [InlineData("962100")]
    [InlineData("9621a")]
    [InlineData("96.21")]
    public async Task A_malformed_sni_code_returns_400_and_never_reaches_scb(string code)
    {
        var scb = WithSni();
        using var factory = CreateHost(scb);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri($"/api/codes/sni/{code}", UriKind.Relative));

        var body = await ReadProblem(response, HttpStatusCode.BadRequest);
        Assert.Equal("invalid_sni", body["code"]!.GetValue<string>());
        Assert.Equal(0, scb.TableCalls);
    }

    [Fact]
    public async Task Scb_failure_returns_502_without_the_upstream_message()
    {
        var scb = new RecordingScbClient
        {
            Failure = new ScbException("GET /kodtabeller/{tabell}: HTTP 503."),
        };
        using var factory = CreateHost(scb);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/codes/municipalities", UriKind.Relative));

        var body = await ReadProblem(response, HttpStatusCode.BadGateway);
        Assert.Equal("scb_unavailable", body["code"]!.GetValue<string>());
        Assert.DoesNotContain("kodtabeller", body.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scb_nightly_load_returns_503()
    {
        var scb = new RecordingScbClient { Failure = new ScbMaintenanceWindowException() };
        using var factory = CreateHost(scb);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/codes/sni/96210", UriKind.Relative));

        var body = await ReadProblem(response, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("scb_unavailable", body["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Openapi_contains_the_code_endpoints()
    {
        using var factory = CreateHost(new RecordingScbClient());
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonObject>(
            new Uri("/openapi/v1.json", UriKind.Relative),
            Json);

        Assert.NotNull(body);
        Assert.NotNull(body["paths"]?["/api/codes/municipalities"]?["get"]);
        Assert.NotNull(body["paths"]?["/api/codes/employee-classes"]?["get"]);
        Assert.NotNull(body["paths"]?["/api/codes/revenue-classes"]?["get"]);
        Assert.NotNull(body["paths"]?["/api/codes/sni/{code}"]?["get"]);
    }

    private static RecordingScbClient WithSni()
    {
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.SniTableName] =
        [
            new CodeItem("96210", "Frisörer och barberare"),
        ];
        return scb;
    }

    private static WebApplicationFactory<Program> CreateHost(RecordingScbClient scb) =>
        ReacherApiFactory.Create(ReacherApiFactory.UnreachableDatabase)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                SearchTestServices.RemoveIngestWorker(services);
                services.RemoveAll<IScbClient>();
                services.AddSingleton<IScbClient>(scb);
            }));

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
}
