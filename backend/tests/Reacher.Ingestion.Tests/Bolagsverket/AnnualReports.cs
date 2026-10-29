using System.Globalization;

namespace Reacher.Ingestion.Tests.Bolagsverket;

/// <summary>
/// Made-up annual reports for the financials ingest tests (hard rule 8). The parser's own
/// edge cases are in <c>IxbrlParserTests</c>; these only need the right years and figures.
/// </summary>
internal static class AnnualReports
{
    /// <summary>The fictional report in Fixtures: 2025-04-30 and 2026-04-30.</summary>
    public static string Fixture() =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Bolagsverket", "Fixtures", "arsredovisning_fiktiv.xhtml"));

    /// <summary>
    /// A small iXBRL report. The first year is the report's own, the rest are comparison
    /// years. A null figure is left out of the report, like an empty line in a K2 report.
    /// </summary>
    public static string Report(params (DateOnly End, long? Revenue, long? NetResult)[] years)
    {
        var contexts = string.Concat(years.Select((year, i) => $"""
            <xbrli:context id="period{i}">
              <xbrli:entity><xbrli:identifier scheme="http://www.bolagsverket.se">559999-0016</xbrli:identifier></xbrli:entity>
              <xbrli:period><xbrli:startDate>{Date(year.End.AddYears(-1).AddDays(1))}</xbrli:startDate><xbrli:endDate>{Date(year.End)}</xbrli:endDate></xbrli:period>
            </xbrli:context>
            """));

        var facts = string.Concat(years.Select((year, i) =>
            Fact("se-gen-base:Nettoomsattning", i, year.Revenue)
            + Fact("se-gen-base:AretsResultat", i, year.NetResult)));

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml"
                  xmlns:ix="http://www.xbrl.org/2013/inlineXBRL"
                  xmlns:ixt="http://www.xbrl.org/inlineXBRL/transformation/2010-04-20"
                  xmlns:xbrli="http://www.xbrl.org/2003/instance"
                  xmlns:iso4217="http://www.xbrl.org/2003/iso4217"
                  xmlns:se-gen-base="http://www.taxonomier.se/se/fr/gen-base/2021-10-31">
            <head><title>Fiktiv</title></head>
            <body>
            <ix:header><ix:resources>
            {contexts}
            <xbrli:unit id="SEK"><xbrli:measure>iso4217:SEK</xbrli:measure></xbrli:unit>
            </ix:resources></ix:header>
            <h2>Resultaträkning</h2>
            {facts}
            </body>
            </html>
            """;
    }

    /// <summary>One figure in whole kronor. A loss has sign="-", as in real reports.</summary>
    private static string Fact(string name, int context, long? value)
    {
        if (value is not { } amount)
        {
            return "";
        }

        var sign = amount < 0 ? " sign=\"-\"" : "";
        var text = Math.Abs(amount).ToString(CultureInfo.InvariantCulture);
        return $"""<p><ix:nonFraction name="{name}" contextRef="period{context}" unitRef="SEK" format="ixt:numspacecomma" scale="0" decimals="0"{sign}>{text}</ix:nonFraction></p>""";
    }

    private static string Date(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
