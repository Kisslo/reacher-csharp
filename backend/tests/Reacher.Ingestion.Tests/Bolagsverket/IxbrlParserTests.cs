using System.Text;
using Microsoft.Extensions.Logging;
using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Tests.Scb;

namespace Reacher.Ingestion.Tests.Bolagsverket;

// Ported from reacher's tests/test_ixbrl.py. The edge cases are built as small documents
// here, so the fixture can look like a real report without warnings.
public sealed class IxbrlParserTests : IDisposable
{
    private const string DocumentId = "FIKTIV-T1-03";

    // Made up, but stands in the fixture's xbrli:identifier. Must never be logged.
    private const string SampleOrgnr = "559999-0016";

    private const string Revenue = "se-gen-base:Nettoomsattning";

    private const string NetResult = "se-gen-base:AretsResultat";

    // The fiscal year end of period0 in Contexts below.
    private static readonly DateOnly FiscalYearEnd = new(2026, 4, 30);

    // The same pairs as Bolagsverket:TagMap in appsettings.json.
    private static readonly Dictionary<string, string> TagMap = new()
    {
        [Revenue] = "revenue",
        [NetResult] = "net_result",
    };

    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose() => _logs.Dispose();

    // --- The fixture -----------------------------------------------------------------

    [Fact]
    public void Sample_gives_revenue_and_net_result_for_both_years_from_the_income_statement()
    {
        // The Flerårsöversikt also has revenue for 2023 and 2024. It is skipped, so only
        // the Resultaträkning's current and comparison year are left.
        Assert.Equal(
            [
                new FinancialValue(new DateOnly(2025, 4, 30), "net_result", -48_210),
                new FinancialValue(new DateOnly(2025, 4, 30), "revenue", 4_388_615),
                new FinancialValue(new DateOnly(2026, 4, 30), "net_result", 126_455),
                new FinancialValue(new DateOnly(2026, 4, 30), "revenue", 4_702_330),
            ],
            Parse(Sample()));
    }

    [Fact]
    public void Similar_tag_names_are_not_mixed_up()
    {
        // AretsResultatEgetKapital is not AretsResultat: the tag must match exactly.
        var facts = Parse(Sample(), new Dictionary<string, string> { [NetResult] = "net_result" });

        Assert.Equal([-48_210, 126_455], facts.Select(fact => fact.Value).Order());
    }

    [Fact]
    public void A_new_field_is_one_tag_map_line()
    {
        // A balance sheet item has an instant context instead of a period.
        var facts = Parse(
            Sample(),
            new Dictionary<string, string> { ["se-gen-base:Aktiekapital"] = "share_capital" });

        Assert.Equal([new FinancialValue(FiscalYearEnd, "share_capital", 25_000)], facts);
    }

    [Fact]
    public void Logs_the_document_id_and_never_the_orgnr()
    {
        // Soliditet is a percentage, and net_result is not in the map, so both are missing.
        Parse(
            Sample(),
            new Dictionary<string, string>
            {
                [Revenue] = "revenue",
                ["se-gen-base:Soliditet"] = "solidity",
            });

        Assert.Contains(_logs.Lines, line => line.Contains(DocumentId, StringComparison.Ordinal));
        Assert.Contains(_logs.Lines, line => line.Contains("solidity", StringComparison.Ordinal));
        Assert.All(_logs.Lines, line =>
        {
            Assert.DoesNotContain(SampleOrgnr, line);
            Assert.DoesNotContain(SampleOrgnr.Replace("-", "", StringComparison.Ordinal), line);
        });
    }

    // --- Small documents for the edge cases -------------------------------------------

    [Theory]
    [InlineData("4 702 330", "0", "0", 4_702_330L)]
    [InlineData("4 702", "3", "-3", 4_702_000L)] // report in tkr
    [InlineData("4,7", "6", "-5", 4_700_000L)] // report in mkr
    [InlineData("4 702 330", "0", "INF", 4_702_330L)] // decimals never changes the value
    [InlineData("0", "0", "0", 0L)] // a reported 0 is a value
    public void Scale_multiplies_and_decimals_is_ignored(
        string text,
        string scale,
        string decimals,
        long expected)
    {
        Assert.Equal([expected], Values(Report(Fact(text, scale: scale, decimals: decimals))));
    }

    public static TheoryData<string, long> SignCases => new()
    {
        { Fact("48 210", NetResult, sign: "-"), -48_210 },
        { "-" + Fact("48 210", NetResult, sign: "-"), -48_210 }, // the dash before the tag is layout
        { "-" + Fact("48 210", NetResult), 48_210 }, // without sign the value is positive
    };

    [Theory]
    [MemberData(nameof(SignCases))]
    public void Only_the_sign_attribute_makes_a_value_negative(string markup, long expected)
    {
        Assert.Equal([expected], Values(Report(markup)));
    }

    [Theory]
    [InlineData("4&#160;702&#160;330", "ixt:numspacecomma", 4_702_330L)] // no-break space
    [InlineData("4&#8239;702&#8239;330", "ixt:numspacecomma", 4_702_330L)] // narrow no-break space
    [InlineData("4.702.330", "ixt:numdotcomma", 4_702_330L)]
    [InlineData("4,702,330", "ixt:numcommadot", 4_702_330L)]
    [InlineData("4 702 330.00", "ixt4:num-dot-decimal", 4_702_330L)]
    [InlineData("4702330", null, 4_702_330L)] // no format = a plain decimal number
    [InlineData("-", "ixt:zerodash", 0L)]
    [InlineData("–", "ixt4:fixed-zero", 0L)]
    public void Number_formats(string text, string? format, long expected)
    {
        Assert.Equal([expected], Values(Report(Fact(text, format: format))));
    }

    public static TheoryData<string, string> UnreadableCases => new()
    {
        { Fact("4 702,50"), "not whole kronor" },
        { Fact("4 702 330", format: "ixt:datelonguk"), "format" },
        { Fact("4 7O2 330"), "format" }, // the letter O
        { Fact(""), "format" },
        { Fact("4 702 330", unitRef: "EUR"), "SEK" },
        { Fact("4 702 330", contextRef: "period9"), "contextRef" },
        { Fact("4 702 330", contextRef: "dim0"), "dimensions" },
        { Fact("4 702 330", scale: "tre"), "scale" },
        { Fact("4 702 330", scale: "29"), "scale" },
    };

    [Theory]
    [MemberData(nameof(UnreadableCases))]
    public void An_unreadable_value_gives_no_value_and_a_warning(string markup, string reason)
    {
        Assert.Empty(Values(Report(markup)));
        Assert.Contains(_logs.Lines, line =>
            line.Contains(DocumentId, StringComparison.Ordinal)
            && line.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_tag_gives_no_value_never_zero()
    {
        var facts = Parse(Report(Fact("4 702 330")));

        Assert.Equal([new FinancialValue(FiscalYearEnd, "revenue", 4_702_330)], facts);
        Assert.Contains(_logs.Lines, line =>
            line.Contains($"Document {DocumentId}: net_result is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Equal_duplicates_become_one()
    {
        Assert.Equal(
            [4_702_330],
            Values(Report(Fact("4 702 330"), Fact("4&#160;702&#160;330"))));
    }

    [Fact]
    public void Conflicting_duplicates_give_no_value()
    {
        // A gap is better than guessing which figure is right.
        Assert.Empty(Values(Report(Fact("4 702 330"), Fact("4 702 331"))));
        Assert.Contains(_logs.Lines, line => line.Contains("different values", StringComparison.Ordinal));
    }

    [Fact]
    public void Tags_outside_the_tag_map_are_ignored()
    {
        Assert.Empty(Values(Report(Fact("158 120", "se-gen-base:ResultatEfterFinansiellaPoster"))));
    }

    [Theory]
    [InlineData("<h2>Flerårsöversikt</h2>")]
    [InlineData("<p><span>Flerårsöversikt (tkr)</span></p>")]
    [InlineData("<h3>  FLERÅRSÖVERSIKT  </h3>")]
    public void The_multi_year_overview_is_skipped_until_the_next_heading(string heading)
    {
        // A real-report bug: tkr figures tagged with scale="0" in the overview.
        var facts = Parse(Report(
            heading,
            Fact("4 702", scale: "0"),
            "<h2>Resultaträkning</h2>",
            Fact("4 702 330")));

        Assert.Equal([new FinancialValue(FiscalYearEnd, "revenue", 4_702_330)], facts);
    }

    [Fact]
    public void A_heading_word_inside_a_sentence_does_not_start_a_section()
    {
        var facts = Parse(Report("<p>Se Flerårsöversikt nedan.</p>", Fact("4 702 330")));

        Assert.Equal([new FinancialValue(FiscalYearEnd, "revenue", 4_702_330)], facts);
    }

    [Theory]
    [InlineData("<html><body><p>trasig</body></html>")]
    [InlineData("%PDF-1.7 inte xhtml")]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>Ingen iXBRL</body></html>")]
    [InlineData("<!DOCTYPE x [<!ENTITY a \"aaaa\">]><html><body>&a;</body></html>")]
    public void A_broken_document_throws_with_the_document_id(string xhtml)
    {
        var error = Assert.Throws<IxbrlParseException>(() => Parse(xhtml));

        Assert.Contains(DocumentId, error.Message);
    }

    // --- Helpers ------------------------------------------------------------------------

    private const string Contexts = """
        <xbrli:context id="period0">
          <xbrli:entity><xbrli:identifier scheme="http://www.bolagsverket.se">559999-0016</xbrli:identifier></xbrli:entity>
          <xbrli:period><xbrli:startDate>2025-05-01</xbrli:startDate><xbrli:endDate>2026-04-30</xbrli:endDate></xbrli:period>
        </xbrli:context>
        <xbrli:context id="dim0">
          <xbrli:entity>
            <xbrli:identifier scheme="http://www.bolagsverket.se">559999-0016</xbrli:identifier>
            <xbrli:segment>
              <xbrldi:explicitMember dimension="se-gen-base:Del">x</xbrldi:explicitMember>
            </xbrli:segment>
          </xbrli:entity>
          <xbrli:period><xbrli:startDate>2025-05-01</xbrli:startDate><xbrli:endDate>2026-04-30</xbrli:endDate></xbrli:period>
        </xbrli:context>
        <xbrli:unit id="SEK"><xbrli:measure>iso4217:SEK</xbrli:measure></xbrli:unit>
        <xbrli:unit id="EUR"><xbrli:measure>iso4217:EUR</xbrli:measure></xbrli:unit>
        """;

    private static string Sample() =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Bolagsverket", "Fixtures", "arsredovisning_fiktiv.xhtml"));

    private static string Report(params string[] body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"
              xmlns:ix="http://www.xbrl.org/2013/inlineXBRL"
              xmlns:ixt="http://www.xbrl.org/inlineXBRL/transformation/2010-04-20"
              xmlns:ixt4="http://www.xbrl.org/inlineXBRL/transformation/2020-02-12"
              xmlns:xbrli="http://www.xbrl.org/2003/instance"
              xmlns:xbrldi="http://xbrl.org/2006/xbrldi"
              xmlns:iso4217="http://www.xbrl.org/2003/iso4217"
              xmlns:se-gen-base="http://www.taxonomier.se/se/fr/gen-base/2021-10-31">
        <head><title>Fiktiv</title></head>
        <body>
        <ix:header><ix:resources>{Contexts}</ix:resources></ix:header>
        {string.Concat(body)}
        </body>
        </html>
        """;

    /// <summary>One <c>ix:nonFraction</c>. An attribute set to null is left out.</summary>
    private static string Fact(
        string text,
        string name = Revenue,
        string? contextRef = "period0",
        string? unitRef = "SEK",
        string? format = "ixt:numspacecomma",
        string? scale = "0",
        string? decimals = "0",
        string? sign = null)
    {
        var attributes = new (string Name, string? Value)[]
        {
            ("contextRef", contextRef),
            ("unitRef", unitRef),
            ("format", format),
            ("scale", scale),
            ("decimals", decimals),
            ("sign", sign),
        };

        var markup = string.Join(
            " ",
            attributes
                .Where(attribute => attribute.Value is not null)
                .Select(attribute => $"{attribute.Name}=\"{attribute.Value}\""));

        return $"<p><ix:nonFraction name=\"{name}\" {markup}>{text}</ix:nonFraction></p>";
    }

    private IReadOnlyList<FinancialValue> Parse(
        string xhtml,
        IReadOnlyDictionary<string, string>? tagMap = null)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(_logs));
        var parser = new IxbrlParser(loggerFactory.CreateLogger<IxbrlParser>());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xhtml));
        return parser.Parse(stream, tagMap ?? TagMap, DocumentId);
    }

    private List<long> Values(string xhtml) => [.. Parse(xhtml).Select(fact => fact.Value)];
}
