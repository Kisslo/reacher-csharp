using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reacher.Api.Companies;

namespace Reacher.Api.Tests;

public sealed class CompanySearchEndpointTests(CompanySearchFixture postgres)
    : IClassFixture<CompanySearchFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] MatchingMunicipalities = ["0180", "0184"];

    [Fact]
    public async Task Search_returns_unique_companies_and_only_matching_workplaces()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(3, body.TotalCount);
        Assert.Equal(3, body.Items.Count);
        var alpha = Assert.Single(body.Items, item => item.Orgnr == "5599001001");
        Assert.Equal(2, alpha.Workplaces.Count);
        Assert.All(alpha.Workplaces, workplace => Assert.Contains(
            workplace.Municipality,
            MatchingMunicipalities));
    }

    [Fact]
    public async Task Search_applies_filters_and_latest_financial_values()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184"
            + "&employeeClass=2&employeeClass=3&revenueClass=2&revenueMin=900000"
            + "&netResultMin=50000&hasFinancials=true");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        var alpha = Assert.Single(body.Items);
        Assert.Equal("5599001001", alpha.Orgnr);
        var latest = Assert.Single(alpha.Financials, financial =>
            financial.PeriodEnd == new DateOnly(2025, 12, 31));
        Assert.Equal(1_000_000, latest.Revenue);
        Assert.Equal(100_000, latest.NetResult);
    }

    [Fact]
    public async Task Search_filters_employee_classes_with_or_semantics()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184&employeeClass=1");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(["5599001027"], body.Items.Select(item => item.Orgnr));

        response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184"
            + "&employeeClass=2&employeeClass=3");

        response.EnsureSuccessStatusCode();
        body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(
            ["5599001001", "5599001019"],
            body.Items.Select(item => item.Orgnr).Order());
    }

    [Fact]
    public async Task Search_filters_revenue_classes_and_combines_filters_with_and_semantics()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184&revenueClass=1");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(["5599001027"], body.Items.Select(item => item.Orgnr));

        response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184"
            + "&employeeClass=2&employeeClass=3&revenueClass=3");

        response.EnsureSuccessStatusCode();
        body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(["5599001019"], body.Items.Select(item => item.Orgnr));
    }

    [Fact]
    public async Task Search_filters_registration_dates_inclusively()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184"
            + "&registeredFrom=2020-01-01&registeredTo=2021-01-01");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(
            ["5599001001", "5599001019"],
            body.Items.Select(item => item.Orgnr).Order());
    }

    [Fact]
    public async Task Search_filters_latest_revenue_and_net_result_ranges()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184"
            + "&revenueMin=600000&revenueMax=1100000");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(["5599001001"], body.Items.Select(item => item.Orgnr));

        response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184"
            + "&netResultMin=-20000&netResultMax=-1");

        response.EnsureSuccessStatusCode();
        body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(["5599001027"], body.Items.Select(item => item.Orgnr));
    }

    [Fact]
    public async Task Search_has_financials_excludes_companies_without_financial_facts()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=1480"
            + "&hasFinancials=true");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.DoesNotContain(body.Items, item => item.Orgnr == "5599001035");
        Assert.Empty(body.Items);
        Assert.Equal(0, body.TotalCount);
    }

    [Fact]
    public async Task Search_sorts_missing_values_last_and_pages_results()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184"
            + "&sort=revenue&order=desc&page=1&pageSize=2");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(3, body.TotalCount);
        Assert.Equal(2, body.Items.Count);
        Assert.Equal("5599001001", body.Items[0].Orgnr);
        Assert.Equal("5599001019", body.Items[1].Orgnr);

        var secondPage = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184"
            + "&sort=revenue&order=desc&page=2&pageSize=2");
        var secondBody = await secondPage.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(secondBody);
        Assert.Equal("5599001027", Assert.Single(secondBody.Items).Orgnr);
    }

    [Theory]
    [InlineData("name", "asc", "5599001001", "5599001019", "5599001027")]
    [InlineData("name", "desc", "5599001027", "5599001019", "5599001001")]
    [InlineData("revenue", "asc", "5599001019", "5599001001", "5599001027")]
    [InlineData("revenue", "desc", "5599001001", "5599001019", "5599001027")]
    [InlineData("netResult", "asc", "5599001027", "5599001001", "5599001019")]
    [InlineData("netResult", "desc", "5599001001", "5599001027", "5599001019")]
    [InlineData("registeredAt", "asc", "5599001001", "5599001019", "5599001027")]
    [InlineData("registeredAt", "desc", "5599001019", "5599001001", "5599001027")]
    [InlineData("employeeClass", "asc", "5599001027", "5599001001", "5599001019")]
    [InlineData("employeeClass", "desc", "5599001019", "5599001001", "5599001027")]
    public async Task Search_sorts_each_supported_field_with_missing_values_last(
        string sort,
        string order,
        string firstOrgnr,
        string secondOrgnr,
        string thirdOrgnr)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            $"/api/companies?sni=96210&municipality=0180&municipality=0184"
            + $"&sort={sort}&order={order}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(
            [firstOrgnr, secondOrgnr, thirdOrgnr],
            body.Items.Select(item => item.Orgnr));
    }

    [Fact]
    public async Task Search_returns_coverage_for_each_requested_municipality()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/api/companies?sni=96210&municipality=0180&municipality=0184&municipality=1480");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(3, body.Coverage.Count);
        Assert.False(Assert.Single(body.Coverage, item => item.Municipality == "0180").Stale);
        Assert.True(Assert.Single(body.Coverage, item => item.Municipality == "0184").Stale);
        var missing = Assert.Single(body.Coverage, item => item.Municipality == "1480");
        Assert.Null(missing.FetchedAt);
        Assert.True(missing.Stale);
    }

    [Theory]
    [InlineData("/api/companies?municipality=0180")]
    [InlineData("/api/companies?sni=96210")]
    [InlineData("/api/companies?sni=9621&municipality=0180")]
    [InlineData("/api/companies?sni=96210&municipality=0180&pageSize=201")]
    public async Task Search_rejects_invalid_query(string requestUri)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(requestUri);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("invalid_search_query", body!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Search_does_not_execute_sql_from_query_values()
    {
        using var client = CreateClient();

        var municipalityPayload = Uri.EscapeDataString("0180' OR 1=1 --");
        var parameterizedResponse = await client.GetAsync(
            $"/api/companies?sni=96210&municipality={municipalityPayload}");

        Assert.Equal(HttpStatusCode.OK, parameterizedResponse.StatusCode);
        var parameterizedBody =
            await parameterizedResponse.Content.ReadFromJsonAsync<CompanySearchResponse>(JsonOptions);
        Assert.NotNull(parameterizedBody);
        Assert.Empty(parameterizedBody.Items);

        var sortPayload = Uri.EscapeDataString("name; DROP TABLE company; --");
        var rejectedResponse = await client.GetAsync(
            $"/api/companies?sni=96210&municipality=0180&sort={sortPayload}");

        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
    }

    [Fact]
    public async Task Openapi_contains_company_search()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(body);
        Assert.NotNull(body["paths"]?["/api/companies"]?["get"]);
        Assert.NotNull(body["components"]?["schemas"]?["CompanySearchResponse"]);
    }

    private HttpClient CreateClient() =>
        ReacherApiFactory.Create(postgres.ConnectionString).CreateClient();
}
