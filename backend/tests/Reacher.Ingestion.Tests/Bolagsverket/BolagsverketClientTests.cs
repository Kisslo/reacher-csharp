using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Tests.Scb;
using static Reacher.Ingestion.Tests.Bolagsverket.FakeBolagsverket;

namespace Reacher.Ingestion.Tests.Bolagsverket;

public sealed class BolagsverketClientTests
{
    private const string OrgNr = BolagsverketTestHost.OrgNr;

    private const string DocumentId = "FIKTIV-T114-2026";

    [Fact]
    public async Task Document_list_fixture_gives_id_and_period_end_for_every_report()
    {
        var api = new FakeBolagsverket(
            _ => Token("token-1"),
            _ => Json(Fixture("dokumentlista.json")));
        using var host = new BolagsverketTestHost(api);

        var documents = await host.Client.GetDocuments(OrgNr, default);

        Assert.Equal(
            ["FIKTIV-T114-2023", "FIKTIV-T114-2024", "FIKTIV-T114-2025", "FIKTIV-T114-2026"],
            documents.Select(document => document.DocumentId));
        Assert.Equal(new DateOnly(2026, 4, 30), documents[3].PeriodEnd);
        Assert.Equal("2026-09-17", documents[3].RegisteredAt);
    }

    [Fact]
    public async Task The_orgnr_is_sent_in_the_body_and_never_in_the_url()
    {
        var api = new FakeBolagsverket(_ => Token("token-1"), _ => Json("""{ "dokument": [] }"""));
        using var host = new BolagsverketTestHost(api);

        await host.Client.GetDocuments(OrgNr, default);

        var request = Assert.Single(api.ApiRequests);
        Assert.Equal("POST", request.Method);
        Assert.Equal($"{BolagsverketTestHost.ApiPath}/dokumentlista", request.Path);
        Assert.Equal($$"""{"identitetsbeteckning":"{{OrgNr}}"}""", request.Body);
        Assert.Equal("Bearer token-1", request.Authorization);
    }

    [Fact]
    public async Task The_token_is_asked_for_with_basic_auth_and_the_scope()
    {
        var api = new FakeBolagsverket(_ => Token("token-1"), _ => Json("""{ "dokument": [] }"""));
        using var host = new BolagsverketTestHost(api);

        await host.Client.GetDocuments(OrgNr, default);

        var request = Assert.Single(api.TokenRequests);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{BolagsverketTestHost.ClientId}:{BolagsverketTestHost.ClientSecret}"));
        Assert.Equal("POST", request.Method);
        Assert.Equal($"Basic {credentials}", request.Authorization);
        Assert.Equal(
            "grant_type=client_credentials&scope=vardefulla-datamangder%3Aread",
            request.Body);
    }

    [Fact]
    public async Task The_token_is_reused_until_a_minute_before_it_expires()
    {
        var api = new FakeBolagsverket(
            _ => Token("token-1", expiresIn: 3600),
            _ => Json("""{ "dokument": [] }"""),
            _ => Json("""{ "dokument": [] }"""),
            _ => Json("""{ "dokument": [] }"""),
            _ => Token("token-2", expiresIn: 3600),
            _ => Json("""{ "dokument": [] }"""));
        using var host = new BolagsverketTestHost(api);

        await host.Client.GetDocuments(OrgNr, default);
        await host.Client.GetDocuments(OrgNr, default);

        // token-1 was asked for at Start and expires at Start + 3600 s.
        api.Clock.Now = FakeBolagsverket.Start.AddSeconds(3539);
        await host.Client.GetDocuments(OrgNr, default);
        api.Clock.Now = FakeBolagsverket.Start.AddSeconds(3540);
        await host.Client.GetDocuments(OrgNr, default);

        Assert.Equal(2, api.TokenRequests.Count);
        Assert.Equal(
            ["Bearer token-1", "Bearer token-1", "Bearer token-1", "Bearer token-2"],
            api.ApiRequests.Select(request => request.Authorization));
    }

    [Fact]
    public async Task A_401_fetches_one_new_token_and_sends_the_request_again()
    {
        var api = new FakeBolagsverket(
            _ => Token("token-1"),
            _ => Status(HttpStatusCode.Unauthorized),
            _ => Token("token-2"),
            _ => Json(Fixture("dokumentlista.json")));
        using var host = new BolagsverketTestHost(api);

        var documents = await host.Client.GetDocuments(OrgNr, default);

        Assert.Equal(4, documents.Count);
        Assert.Equal(2, api.TokenRequests.Count);
        Assert.Equal(
            ["Bearer token-1", "Bearer token-2"],
            api.ApiRequests.Select(request => request.Authorization));
    }

    [Fact]
    public async Task A_second_401_fails_without_a_third_token()
    {
        var api = new FakeBolagsverket(
            _ => Token("token-1"),
            _ => Status(HttpStatusCode.Unauthorized),
            _ => Token("token-2"),
            _ => Status(HttpStatusCode.Unauthorized));
        using var host = new BolagsverketTestHost(api);

        var error = await Assert.ThrowsAsync<BolagsverketAuthException>(
            () => host.Client.GetDocuments(OrgNr, default));

        Assert.Equal(2, api.TokenRequests.Count);
        Assert.Equal(4, api.Requests.Count);
        Assert.Contains("'Bolagsverket:ClientId'", error.Message);
        Assert.DoesNotContain(OrgNr, error.ToString());
    }

    [Fact]
    public async Task Wrong_credentials_name_the_settings_and_never_the_values()
    {
        var api = new FakeBolagsverket(_ => Status(HttpStatusCode.Unauthorized));
        using var host = new BolagsverketTestHost(api);

        var error = await Assert.ThrowsAsync<BolagsverketAuthException>(
            () => host.Client.GetDocuments(OrgNr, default));

        Assert.Contains("'Bolagsverket:ClientSecret'", error.Message);
        Assert.DoesNotContain(BolagsverketTestHost.ClientId, error.ToString());
        Assert.DoesNotContain(BolagsverketTestHost.ClientSecret, error.ToString());
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task A_document_list_entry_without_id_or_period_is_skipped()
    {
        var api = new FakeBolagsverket(
            _ => Token("token-1"),
            _ => Json("""
                { "dokument": [
                    { "dokumentId": "FIKTIV-1", "rapporteringsperiodTom": "2025-12-31" },
                    { "dokumentId": "FIKTIV-2" },
                    { "rapporteringsperiodTom": "2024-12-31" },
                    { "dokumentId": "FIKTIV-3", "rapporteringsperiodTom": "inte ett datum" }
                ] }
                """));
        using var host = new BolagsverketTestHost(api);

        var documents = await host.Client.GetDocuments(OrgNr, default);

        Assert.Equal("FIKTIV-1", Assert.Single(documents).DocumentId);
    }

    [Fact]
    public async Task A_company_without_documents_gives_an_empty_list()
    {
        var api = new FakeBolagsverket(_ => Token("token-1"), _ => Status(HttpStatusCode.NotFound));
        using var host = new BolagsverketTestHost(api);

        Assert.Empty(await host.Client.GetDocuments(OrgNr, default));
    }

    [Theory]
    [InlineData("9001011234")] // personnummer: the third digit is a month, 0 or 1
    [InlineData("199001011234")] // personnummer with century
    [InlineData("165599990016")] // SCB's peOrgNr: the ingest strips the 16 first
    [InlineData("559999-0016")] // with a dash
    [InlineData("")]
    public async Task Only_a_10_digit_organisation_number_is_sent(string orgnr)
    {
        var api = new FakeBolagsverket();
        using var host = new BolagsverketTestHost(api);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => host.Client.GetDocuments(orgnr, default));

        Assert.Empty(api.Requests);
        if (orgnr.Length > 0)
        {
            Assert.DoesNotContain(orgnr, error.Message);
        }
    }

    [Fact]
    public async Task A_zip_with_one_xhtml_gives_one_stream_that_the_parser_can_read()
    {
        var api = new FakeBolagsverket(
            _ => Token("token-1"),
            _ => Zip(
                ("arsredovisning.xhtml", Fixture("arsredovisning_fiktiv.xhtml")),
                ("info.txt", "x")));
        using var host = new BolagsverketTestHost(api);

        var files = await host.Client.Download(DocumentId, default);

        using var file = Assert.Single(files);
        var tagMap = new Dictionary<string, string> { ["se-gen-base:Nettoomsattning"] = "revenue" };
        var parser = new IxbrlParser(NullLogger<IxbrlParser>.Instance);
        Assert.NotEmpty(parser.Parse(file, tagMap, DocumentId));
        Assert.Equal(
            $"{BolagsverketTestHost.ApiPath}/dokument/{DocumentId}",
            Assert.Single(api.ApiRequests).Path);
    }

    [Fact]
    public async Task A_zip_without_xhtml_gives_no_streams_and_a_warning()
    {
        var logs = new CapturingLoggerProvider();
        var api = new FakeBolagsverket(_ => Token("token-1"), _ => Zip(("info.txt", "x")));
        using var host = new BolagsverketTestHost(api, logs);

        var files = await host.Client.Download(DocumentId, default);

        Assert.Empty(files);
        Assert.Contains(logs.Lines, line => line.Contains(
            $"Document {DocumentId}: the zip has no xhtml file",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_document_that_is_not_a_zip_is_returned_as_it_is()
    {
        var api = new FakeBolagsverket(_ => Token("token-1"), _ => Json("<html/>"));
        using var host = new BolagsverketTestHost(api);

        var files = await host.Client.Download(DocumentId, default);

        using var reader = new StreamReader(Assert.Single(files));
        Assert.Equal("<html/>", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task A_missing_document_gives_no_streams()
    {
        var api = new FakeBolagsverket(_ => Token("token-1"), _ => Status(HttpStatusCode.NotFound));
        using var host = new BolagsverketTestHost(api);

        Assert.Empty(await host.Client.Download(DocumentId, default));
    }

    [Fact]
    public void A_file_larger_than_the_limit_is_skipped()
    {
        var zip = ZipBytes(("small.xhtml", "12345"), ("large.xhtml", "1234567890"));

        var files = ReportFiles.Extract(zip, DocumentId, NullLogger.Instance, maxBytes: 5);

        Assert.Equal(5, Assert.Single(files).Length);
    }

    [Fact]
    public async Task A_503_is_retried_and_succeeds()
    {
        var api = new FakeBolagsverket(
            _ => Token("token-1"),
            _ => RetryNow(HttpStatusCode.ServiceUnavailable),
            _ => Json("""{ "dokument": [] }"""));
        using var host = new BolagsverketTestHost(api);

        Assert.Empty(await host.Client.GetDocuments(OrgNr, default));
        Assert.Equal(2, api.ApiRequests.Count);
    }

    [Fact]
    public async Task Errors_show_the_document_id()
    {
        var api = new FakeBolagsverket(
            _ => Token("token-1"),
            _ => Status(HttpStatusCode.BadRequest));
        using var host = new BolagsverketTestHost(api);

        var error = await Assert.ThrowsAsync<BolagsverketException>(
            () => host.Client.Download(DocumentId, default));

        Assert.Equal($"GET /dokument/{DocumentId}: HTTP 400.", error.Message);
    }

    [Fact]
    public async Task The_orgnr_the_token_and_the_credentials_never_reach_the_log()
    {
        var logs = new CapturingLoggerProvider();
        var api = new FakeBolagsverket(
            _ => RetryNow(HttpStatusCode.ServiceUnavailable),
            _ => Token("secret-token-1"),
            _ => RetryNow(HttpStatusCode.TooManyRequests),
            _ => Json("""{ "dokument": [{ "dokumentId": "FIKTIV-2" }] }"""),
            _ => Status(HttpStatusCode.NotFound));
        using var host = new BolagsverketTestHost(api, logs);

        await host.Client.GetDocuments(OrgNr, default);
        await host.Client.Download(DocumentId, default);

        // The retries and the skipped entry were logged, so logging is on in this test.
        Assert.Contains(logs.Lines, line => line.Contains("503", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, line => line.Contains("429", StringComparison.Ordinal));
        Assert.Contains(
            logs.Lines,
            line => line.Contains("without dokumentId", StringComparison.Ordinal));
        Assert.All(logs.Lines, line =>
        {
            Assert.DoesNotContain(OrgNr, line);
            Assert.DoesNotContain("secret-token-1", line);
            Assert.DoesNotContain(BolagsverketTestHost.ClientId, line);
            Assert.DoesNotContain(BolagsverketTestHost.ClientSecret, line);
        });
    }

    [Fact]
    public void Requests_are_booked_one_second_apart()
    {
        var clock = new ManualTimeProvider(FakeBolagsverket.Start);
        var limiter = new BolagsverketRateLimiter(clock);

        Assert.Equal(TimeSpan.Zero, limiter.Reserve());
        Assert.Equal(TimeSpan.FromSeconds(1), limiter.Reserve());
        Assert.Equal(TimeSpan.FromSeconds(2), limiter.Reserve());

        // After a quiet period there is no wait, and no backlog is saved up.
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.Zero, limiter.Reserve());
        Assert.Equal(TimeSpan.FromSeconds(1), limiter.Reserve());
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Bolagsverket", "Fixtures", name));
}
