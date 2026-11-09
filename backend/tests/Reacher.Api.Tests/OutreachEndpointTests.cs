using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Reacher.Api.Companies;

namespace Reacher.Api.Tests;

public sealed class OutreachEndpointTests(CompanySearchFixture postgres)
    : IClassFixture<CompanySearchFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Orgnr = "5599001001";

    [Fact]
    public async Task Outreach_can_be_created_updated_and_cleared()
    {
        await ResetAsync();
        using var client = CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Orgnr}/outreach",
            new { called = true, willing = false, note = "Ring nästa vecka" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var search = await client.GetFromJsonAsync<CompanySearchResponse>(
            "/api/companies?sni=96210&municipality=0180");
        var item = Assert.Single(search!.Items, item => item.Orgnr == Orgnr);
        Assert.True(item.CalledAt.HasValue);
        Assert.False(item.Willing);
        Assert.Equal("Ring nästa vecka", item.Note);
        var calledAt = item.CalledAt;

        response = await client.PutAsJsonAsync(
            $"/api/companies/{Orgnr}/outreach",
            new { called = true, willing = true, note = "Skicka mail med info" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        search = await client.GetFromJsonAsync<CompanySearchResponse>(
            "/api/companies?sni=96210&municipality=0180");
        item = Assert.Single(search!.Items, item => item.Orgnr == Orgnr);
        Assert.Equal(calledAt, item.CalledAt);
        Assert.True(item.Willing);
        Assert.Equal("Skicka mail med info", item.Note);

        response = await client.PutAsJsonAsync(
            $"/api/companies/{Orgnr}/outreach",
            new { called = false, willing = (bool?)null, note = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.False(await db.CompanyOutreach.AnyAsync(item => item.Orgnr == Orgnr));
    }

    [Fact]
    public async Task Willingness_update_does_not_mark_company_called()
    {
        await ResetAsync();
        using var client = CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Orgnr}/outreach",
            new { called = false, willing = true, note = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var search = await client.GetFromJsonAsync<CompanySearchResponse>(
            "/api/companies?sni=96210&municipality=0180&willing=true");
        var item = Assert.Single(search!.Items);
        Assert.Null(item.CalledAt);
        Assert.True(item.Willing);
    }

    [Fact]
    public async Task Outreach_filters_distinguish_called_and_willing_states()
    {
        await ResetAsync();
        using var client = CreateClient();

        await client.PutAsJsonAsync(
            "/api/companies/5599001001/outreach",
            new { called = false, willing = false, note = (string?)null });
        await client.PutAsJsonAsync(
            "/api/companies/5599001019/outreach",
            new { called = true, willing = (bool?)null, note = (string?)null });

        var called = await client.GetFromJsonAsync<CompanySearchResponse>(
            "/api/companies?sni=96210&municipality=0180&municipality=0184&called=true");
        Assert.Equal(["5599001019"], called!.Items.Select(item => item.Orgnr));

        var notCalled = await client.GetFromJsonAsync<CompanySearchResponse>(
            "/api/companies?sni=96210&municipality=0180&municipality=0184&called=false");
        Assert.Contains(notCalled!.Items, item => item.Orgnr == "5599001001");
        Assert.Contains(notCalled.Items, item => item.Orgnr == "5599001027");
        Assert.DoesNotContain(notCalled.Items, item => item.Orgnr == "5599001019");

        var willingTrue = await client.GetFromJsonAsync<CompanySearchResponse>(
            "/api/companies?sni=96210&municipality=0180&municipality=0184&willing=true");
        Assert.Empty(willingTrue!.Items);

        var willingFalse = await client.GetFromJsonAsync<CompanySearchResponse>(
            "/api/companies?sni=96210&municipality=0180&municipality=0184&willing=false");
        Assert.Equal(["5599001001"], willingFalse!.Items.Select(item => item.Orgnr));
    }

    [Theory]
    [InlineData("https://example.test")]
    [InlineData("Call 199001011234 later")]
    [InlineData("Call 1990010123 later")]
    public async Task Outreach_rejects_urls_and_personnummer_like_notes(string note)
    {
        await ResetAsync();
        using var client = CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Orgnr}/outreach",
            new { called = true, willing = (bool?)null, note });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("note_rejected", body!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Outreach_rejects_notes_over_500_characters()
    {
        await ResetAsync();
        using var client = CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Orgnr}/outreach",
            new { called = true, willing = (bool?)null, note = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Outreach_returns_not_found_for_unknown_company()
    {
        await ResetAsync();
        using var client = CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/companies/5599999999/outreach",
            new { called = true, willing = (bool?)null, note = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("company_not_found", body!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Openapi_contains_outreach_endpoint()
    {
        await ResetAsync();
        using var client = CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(body);
        Assert.NotNull(body["paths"]?["/api/companies/{orgnr}/outreach"]?["put"]);
    }

    private HttpClient CreateClient() =>
        ReacherApiFactory.Create(postgres.ConnectionString).CreateClient();

    private async Task ResetAsync()
    {
        await using var db = postgres.CreateContext();
        await db.CompanyOutreach.ExecuteDeleteAsync();
    }
}
