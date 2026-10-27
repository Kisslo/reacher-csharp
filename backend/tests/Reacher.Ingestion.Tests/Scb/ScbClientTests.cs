using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text.Json;
using Reacher.Ingestion.Scb;
using static Reacher.Ingestion.Tests.Scb.FakeHttpHandler;

namespace Reacher.Ingestion.Tests.Scb;

public sealed class ScbClientTests
{
    private const string OrgNr = "165599001236";

    [Fact]
    public async Task Paging_reads_three_pages_then_stops_when_hasMore_is_false()
    {
        var handler = new FakeHttpHandler(
            _ => Json(Page(nextCursorId: 10, hasMore: true, 90000001, 90000002)),
            _ => Json(Page(nextCursorId: 20, hasMore: true, 90000003)),
            // SCB still sends a cursor on the last page. Only hasMore decides.
            _ => Json(Page(nextCursorId: 30, hasMore: false, 90000004)));
        using var host = new ScbTestHost(handler);

        var cfars = new List<string?>();
        await foreach (var workplace in host.Client.GetWorkplacesBySni("96210", default))
        {
            cfars.Add(workplace.CfarNr);
        }

        Assert.Equal(["90000001", "90000002", "90000003", "90000004"], cfars);
        Assert.Equal(
            [
                "/v1/arbetsstallen/naringsgren/96210?limit=2",
                "/v1/arbetsstallen/naringsgren/96210?limit=2&cursorId=10",
                "/v1/arbetsstallen/naringsgren/96210?limit=2&cursorId=20",
            ],
            handler.Paths);
    }

    [Fact]
    public async Task Paging_fails_instead_of_looping_when_the_cursor_repeats()
    {
        var handler = new FakeHttpHandler(
            _ => Json(Page(nextCursorId: 10, hasMore: true, 90000001)),
            _ => Json(Page(nextCursorId: 10, hasMore: true, 90000002)));
        using var host = new ScbTestHost(handler);

        await Assert.ThrowsAsync<ScbException>(async () =>
        {
            await foreach (var _ in host.Client.GetWorkplacesBySni("96210", default))
            {
            }
        });
    }

    [Fact]
    public async Task Workplace_page_fixture_keeps_raw_values_and_visiting_address()
    {
        var handler = new FakeHttpHandler(
            _ => Json(Fixture("ae_naringsgren_96210.json")),
            _ => Json(Page(nextCursorId: null, hasMore: false)));
        using var host = new ScbTestHost(handler);

        var workplaces = new List<WorkplacePartial>();
        await foreach (var workplace in host.Client.GetWorkplacesBySni("96210", default))
        {
            workplaces.Add(workplace);
        }

        Assert.Equal(2, workplaces.Count);
        var first = workplaces[0];
        Assert.Equal("90000001", first.CfarNr);
        Assert.Equal(OrgNr, first.PeOrgNr);
        Assert.Equal("1", first.AeStat);
        Assert.Equal("2", first.AnstKl);
        Assert.Equal("1", first.ReklamSparrTyp);
        Assert.Equal("96210", first.PrimarNaringsgren?.Naringsgren);
        Assert.Equal("0180", first.Belagenhetsadress?.Kommun);
        Assert.Equal("Påhittade gatan 1", first.Belagenhetsadress?.BGatuAdress);
        // Raw in the client: the ingest trims, not the client.
        Assert.Equal(" ", workplaces[1].Ben);
    }

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("2")]
    public async Task AnstKl_as_string_or_number_gives_the_string_2(string anstKl)
    {
        var handler = new FakeHttpHandler(
            _ => Json($$"""{ "cfarNr": 90000001, "anstKl": {{anstKl}} }"""));
        using var host = new ScbTestHost(handler);

        var workplace = await host.Client.GetWorkplace("90000001", default);

        Assert.Equal("2", workplace?.AnstKl);
    }

    [Theory]
    [InlineData("\"01\"", "01")]
    [InlineData("1", "1")]
    [InlineData("\"\"", "")]
    [InlineData("null", null)]
    public void Codes_are_text_with_no_other_change(string json, string? expected)
    {
        var item = JsonSerializer.Deserialize<CodeItem>(
            $$"""{ "kod": {{json}}, "klartext": "x" }""",
            ScbJson.Options);

        Assert.Equal(expected, item?.Kod);
    }

    [Fact]
    public async Task Full_workplace_fixture_turns_number_codes_into_text()
    {
        var handler = new FakeHttpHandler(_ => Json(Fixture("ae_full.json")));
        using var host = new ScbTestHost(handler);

        var workplace = await host.Client.GetWorkplace("90000001", default);

        Assert.NotNull(workplace);
        Assert.Equal("90000001", workplace.CfarNr);
        Assert.Equal("1", workplace.AeStat);
        Assert.Equal("2", workplace.AnstKl);
        Assert.Equal(["/v1/arbetsstallen/90000001/full"], handler.Paths);
    }

    [Fact]
    public async Task Legal_unit_fixture_maps_the_fields_ingest_needs()
    {
        var handler = new FakeHttpHandler(_ => Json(Fixture("je_full.json")));
        using var host = new ScbTestHost(handler);

        var company = await host.Client.GetLegalUnit(OrgNr, default);

        Assert.NotNull(company);
        Assert.Equal("49", company.Jurform);
        Assert.Equal("1", company.FtgStat);
        Assert.Equal("1", company.ReklamSparrTyp);
        Assert.Equal("3", company.OmsKl);
        Assert.Equal(2024, company.OmsAr);
        Assert.Equal("Påhittade Saxen AB", company.Foretagsnamn);
        Assert.Equal("2019-03-20T15:16:00.000Z", company.RegDat);
    }

    [Fact]
    public async Task Not_found_gives_null()
    {
        var handler = new FakeHttpHandler(
            _ => Status(HttpStatusCode.NotFound),
            _ => Status(HttpStatusCode.NotFound));
        using var host = new ScbTestHost(handler);

        Assert.Null(await host.Client.GetWorkplace("90000001", default));
        Assert.Null(await host.Client.GetLegalUnit(OrgNr, default));
    }

    [Fact]
    public async Task Legal_unit_refuses_anything_but_a_legal_person_without_a_request()
    {
        var handler = new FakeHttpHandler();
        using var host = new ScbTestHost(handler);

        // A 10-digit orgnr without the 16 prefix: not on the allow-list, so refused.
        await Assert.ThrowsAsync<ArgumentException>(
            () => host.Client.GetLegalUnit("5599001236", default));
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Api_key_goes_in_the_header_and_never_in_the_url()
    {
        var handler = new FakeHttpHandler(_ => Json("""[{ "kod": 2, "klartext": "1-4" }]"""));
        using var host = new ScbTestHost(handler);

        var table = await host.Client.GetCodeTable("anstklkoder", default);

        Assert.Equal("2", Assert.Single(table).Kod);
        Assert.Equal(ScbTestHost.ApiKey, Assert.Single(handler.ApiKeys));
        Assert.DoesNotContain(ScbTestHost.ApiKey, Assert.Single(handler.Paths));
    }

    [Fact]
    public async Task A_429_with_Retry_After_1_is_retried_and_succeeds()
    {
        var handler = new FakeHttpHandler(
            _ => RetryAfter(HttpStatusCode.TooManyRequests, seconds: 1),
            _ => Json("""{ "count": 18415, "path": "/v1/arbetsstallen/naringsgren/96210" }"""));
        using var host = new ScbTestHost(handler);

        var count = await host.Client.CountWorkplacesBySni("96210", default);

        Assert.Equal(18415, count);
        Assert.Equal(2, handler.Paths.Count);
    }

    [Fact]
    public async Task Tls_errors_are_not_retried()
    {
        var handler = new FakeHttpHandler(_ => throw new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established.",
            new AuthenticationException()));
        using var host = new ScbTestHost(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => host.Client.CountWorkplacesBySni("96210", default));
        Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task A_wrong_key_fails_at_once_and_names_the_setting_not_the_value()
    {
        var handler = new FakeHttpHandler(_ => Status(HttpStatusCode.Unauthorized));
        using var host = new ScbTestHost(handler);

        var error = await Assert.ThrowsAsync<ScbException>(
            () => host.Client.CountWorkplacesBySni("96210", default));

        Assert.Contains("'Scb:ApiKey'", error.Message);
        Assert.DoesNotContain(ScbTestHost.ApiKey, error.Message);
        Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task Errors_name_the_route_template_never_the_orgnr()
    {
        var handler = new FakeHttpHandler(_ => Status(HttpStatusCode.BadRequest));
        using var host = new ScbTestHost(handler);

        var error = await Assert.ThrowsAsync<ScbException>(
            () => host.Client.GetLegalUnit(OrgNr, default));

        Assert.Contains("/juridiskaenheter/{peOrgNr}/full", error.Message);
        Assert.DoesNotContain(OrgNr, error.ToString());
    }

    [Fact]
    public async Task Urls_and_the_key_never_reach_the_log_even_on_a_retry()
    {
        var logs = new CapturingLoggerProvider();
        var handler = new FakeHttpHandler(
            _ => RetryAfter(HttpStatusCode.ServiceUnavailable, seconds: 0),
            _ => Json(Fixture("je_full.json")));
        using var host = new ScbTestHost(handler, logs: logs);

        await host.Client.GetLegalUnit(OrgNr, default);

        // The retry was logged, so logging is switched on in this test.
        Assert.Contains(logs.Lines, line => line.Contains("503", StringComparison.Ordinal));
        Assert.All(logs.Lines, line =>
        {
            Assert.DoesNotContain(OrgNr, line);
            Assert.DoesNotContain("juridiskaenheter", line);
            Assert.DoesNotContain(ScbTestHost.ApiKey, line);
        });
    }

    [Fact]
    public async Task No_request_is_sent_during_the_nightly_window()
    {
        var handler = new FakeHttpHandler();
        // 03:10 UTC in November is 04:10 in Stockholm.
        using var host = new ScbTestHost(
            handler,
            now: new DateTimeOffset(2026, 11, 3, 3, 10, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<ScbMaintenanceWindowException>(
            () => host.Client.CountWorkplacesBySni("96210", default));
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData("2026-11-03T02:59:59Z", false)] // 03:59:59 CET
    [InlineData("2026-11-03T03:00:00Z", true)] // 04:00 CET
    [InlineData("2026-11-03T03:29:59Z", true)] // 04:29:59 CET
    [InlineData("2026-11-03T03:30:00Z", false)] // 04:30 CET
    [InlineData("2026-07-01T02:15:00Z", true)] // 04:15 CEST, summer time
    [InlineData("2026-07-01T03:15:00Z", false)] // 05:15 CEST
    public void Maintenance_window_is_04_00_to_04_30_Swedish_time(string utc, bool closed)
    {
        var moment = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);

        Assert.Equal(closed, ScbMaintenanceWindow.Contains(moment));
    }

    [Fact]
    public void Retry_After_is_capped_at_60_seconds()
    {
        using var slow = RetryAfter(HttpStatusCode.TooManyRequests, seconds: 3600);
        using var none = Status(HttpStatusCode.ServiceUnavailable);

        Assert.Equal(TimeSpan.FromSeconds(60), ScbResilience.RetryAfter(slow));
        Assert.Null(ScbResilience.RetryAfter(none));
    }

    private static HttpResponseMessage RetryAfter(HttpStatusCode status, int seconds) =>
        new(status)
        {
            Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds)) },
        };

    private static string Page(int? nextCursorId, bool hasMore, params int[] cfars) =>
        JsonSerializer.Serialize(new
        {
            pagination = new { nextCursorId, limit = 2, hasMore },
            arbetsstallen = cfars.Select(cfarNr => new { cfarNr, peOrgNr = OrgNr }),
        });

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scb", "Fixtures", name));
}
