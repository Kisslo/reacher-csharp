using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// Reads financial figures from an annual report in iXBRL (D14). Pure: xhtml in,
/// <see cref="FinancialValue"/>s out. No network, no database and no orgnr.
/// Ported from reacher's <c>ixbrl.py</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only the <c>ix:nonFraction</c> tags in the tag map are read. A missing tag gives
/// no value, never 0.</item>
/// <item>Value = the text read by <c>format</c>, times 10^<c>scale</c>, negative if
/// <c>sign="-"</c>. A minus sign in the text next to the tag is layout and is ignored.
/// <c>decimals</c> says how exact the figure is and never changes the value.</item>
/// <item>Only contexts without dimensions (segment/scenario), only the unit
/// <c>iso4217:SEK</c> and only whole kronor. Anything else is skipped with a warning.</item>
/// <item>Values under the Flerårsöversikt heading are skipped. In real reports it is often
/// in tkr and sometimes has the wrong <c>scale</c>. The Resultaträkning has the same
/// figures for the current and the comparison year.</item>
/// <item>Equal duplicates become one. Different values for the same year and key give no
/// value: a gap is better than a guess.</item>
/// <item>Logs the document id, never the orgnr. The report's <c>xbrli:identifier</c> is
/// the orgnr.</item>
/// </list>
/// </remarks>
public sealed partial class IxbrlParser
{
    private const string Sek = "iso4217:SEK";

    private const string MultiYearOverview = "Flerårsöversikt";

    // decimal holds 10^28 at most, so a larger scale cannot be a real amount.
    private const int MaxScale = 28;

    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

    private static readonly XNamespace Ix = "http://www.xbrl.org/2013/inlineXBRL";

    private static readonly XNamespace Xbrli = "http://www.xbrl.org/2003/instance";

    // Headings in the order a K2 report usually has them. A section runs until the next
    // one, so the Flerårsöversikt ends at whichever of these comes after it.
    private static readonly string[] Headings =
    [
        "Förvaltningsberättelse",
        MultiYearOverview,
        "Förändringar i eget kapital",
        "Resultatdisposition",
        "Resultaträkning",
        "Balansräkning",
        "Kassaflödesanalys",
        "Noter",
        "Underskrifter",
    ];

    // format without its prefix -> (thousands separators, decimal sign). The names come
    // from the XBRL Transformation Registry, where versions 1-4 name the same thing
    // differently. An unknown format gives no value.
    private static readonly Dictionary<string, (string Thousands, char DecimalSign)> Formats =
        new(StringComparer.Ordinal)
        {
            ["numspacecomma"] = (" ", ','), // 1 234 567,89 (Bolagsverket's usual one)
            ["numdotcomma"] = (".", ','), // 1.234.567,89
            ["numcomma"] = ("", ','), // 1234567,89
            ["numspacedot"] = (" ", '.'), // 1 234 567.89
            ["numcommadot"] = (",", '.'), // 1,234,567.89
            ["numdotdecimal"] = (" ,", '.'),
            ["num-dot-decimal"] = (" ,", '.'),
            ["numcommadecimal"] = (" .", ','),
            ["num-comma-decimal"] = (" .", ','),
        };

    // No format attribute: the text is a plain decimal number.
    private static readonly (string Thousands, char DecimalSign) PlainNumber = ("", '.');

    // The formats where a dash or an empty cell means a reported 0.
    private static readonly HashSet<string> ZeroFormats =
        new(["zerodash", "numdash", "fixed-zero"], StringComparer.Ordinal);

    private readonly ILogger<IxbrlParser> _logger;

    public IxbrlParser(ILogger<IxbrlParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Every value for the tags in <paramref name="tagMap"/>, sorted by period end and key.
    /// A report usually holds the current and the comparison year, and both are returned.
    /// Which ones are stored is up to the financials ingest (T1-06).
    /// </summary>
    /// <param name="xhtml">The report. Read but not closed.</param>
    /// <param name="tagMap">iXBRL tag, e.g. <c>se-gen-base:Nettoomsattning</c> -> fact name.</param>
    /// <param name="documentId">Bolagsverket's document id, used in logs and errors.</param>
    /// <exception cref="IxbrlParseException">Not valid xhtml, or not iXBRL.</exception>
    public IReadOnlyList<FinancialValue> Parse(
        Stream xhtml,
        IReadOnlyDictionary<string, string> tagMap,
        string documentId)
    {
        var root = Load(xhtml, documentId);
        if (!root.Descendants(Ix + "header").Any())
        {
            throw new IxbrlParseException(
                $"Document {documentId}: no ix:header, so not iXBRL 1.1.");
        }

        var periods = ReadPeriods(root);
        var sekUnits = ReadSekUnits(root);

        var found = new Dictionary<(DateOnly PeriodEnd, string Key), HashSet<long>>();
        var inMultiYearOverview = false;

        // Descendants() walks the document in reading order, so a heading is always seen
        // before the values under it.
        foreach (var element in root.Descendants())
        {
            if (HeadingOf(element) is { } heading)
            {
                inMultiYearOverview = heading == MultiYearOverview;
                continue;
            }

            if (element.Name != Ix + "nonFraction" || inMultiYearOverview)
            {
                continue;
            }

            var tag = (string?)element.Attribute("name") ?? "";
            if (!tagMap.TryGetValue(tag, out var key))
            {
                continue;
            }

            if (!TryRead(element, periods, sekUnits, out var periodEnd, out var value, out var reason))
            {
                LogSkipped(_logger, documentId, tag, (string?)element.Attribute("contextRef"), reason);
                continue;
            }

            if (!found.TryGetValue((periodEnd, key), out var values))
            {
                values = [];
                found[(periodEnd, key)] = values;
            }

            values.Add(value);
        }

        var facts = new List<FinancialValue>();
        foreach (var ((periodEnd, key), values) in found
            .OrderBy(pair => pair.Key.PeriodEnd)
            .ThenBy(pair => pair.Key.Key, StringComparer.Ordinal))
        {
            if (values.Count > 1)
            {
                LogConflict(_logger, documentId, key, periodEnd);
                continue;
            }

            facts.Add(new FinancialValue(periodEnd, key, values.Single()));
        }

        foreach (var key in tagMap.Values
            .Distinct(StringComparer.Ordinal)
            .Except(facts.Select(fact => fact.Key), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            // Normal for e.g. a holding company without revenue. No row, never 0.
            LogMissing(_logger, documentId, key);
        }

        return facts;
    }

    private static XElement Load(Stream xhtml, string documentId)
    {
        // A DOCTYPE is skipped, never processed, and nothing outside the document is
        // fetched. That also stops entity expansion attacks ("billion laughs").
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };

        try
        {
            using var reader = XmlReader.Create(xhtml, settings);
            return XDocument.Load(reader).Root
                ?? throw new IxbrlParseException($"Document {documentId}: empty document.");
        }
        catch (XmlException e)
        {
            // Line and position only. The inner exception is left out because its message
            // can quote text from the document.
            throw new IxbrlParseException(
                $"Document {documentId}: not valid xhtml (line {e.LineNumber}, "
                + $"position {e.LinePosition}).");
        }
    }

    /// <summary>contextRef -> end date. Contexts with dimensions are left out.</summary>
    private static Dictionary<string, DateOnly> ReadPeriods(XElement root)
    {
        var periods = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var context in root.Descendants(Xbrli + "context"))
        {
            if (context.Descendants(Xbrli + "segment").Any()
                || context.Descendants(Xbrli + "scenario").Any())
            {
                continue;
            }

            // endDate for a fiscal year, instant for a balance sheet date.
            var period = context.Element(Xbrli + "period");
            var end = period?.Element(Xbrli + "endDate") ?? period?.Element(Xbrli + "instant");
            var text = end?.Value.Trim() ?? "";

            // [..10]: a date can be written as 2026-04-30T00:00:00.
            if (text.Length >= 10
                && DateOnly.TryParseExact(
                    text[..10],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var periodEnd))
            {
                periods[(string?)context.Attribute("id") ?? ""] = periodEnd;
            }
        }

        return periods;
    }

    /// <summary>unitRef of the units that are exactly kronor (not percent, count or kr/share).</summary>
    private static HashSet<string> ReadSekUnits(XElement root) =>
        root.Descendants(Xbrli + "unit")
            .Where(unit => unit.Descendants(Xbrli + "measure")
                .Select(measure => measure.Value.Trim())
                .SequenceEqual([Sek]))
            .Select(unit => (string?)unit.Attribute("id") ?? "")
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The known heading this element is, or null. Only text-only XHTML elements count.</summary>
    private static string? HeadingOf(XElement element)
    {
        if (element.Name.Namespace != Xhtml || element.HasElements)
        {
            return null;
        }

        var text = Whitespace().Replace(element.Value, " ").Trim();

        // "Flerårsöversikt (tkr)" is the same heading.
        return Array.Find(Headings, heading =>
            text.Equals(heading, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(heading + " (", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryRead(
        XElement fact,
        Dictionary<string, DateOnly> periods,
        HashSet<string> sekUnits,
        out DateOnly periodEnd,
        out long value,
        [NotNullWhen(false)] out string? reason)
    {
        value = 0;
        reason = null;

        if (!periods.TryGetValue((string?)fact.Attribute("contextRef") ?? "", out periodEnd))
        {
            reason = "unknown contextRef or a context with dimensions";
            return false;
        }

        if (!sekUnits.Contains((string?)fact.Attribute("unitRef") ?? ""))
        {
            reason = "the unit is not SEK";
            return false;
        }

        var format = (string?)fact.Attribute("format");
        if (ReadNumber(fact.Value, format) is not { } number)
        {
            reason = $"the text cannot be read with format {format}";
            return false;
        }

        if (!int.TryParse(
                (string?)fact.Attribute("scale") ?? "0",
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var scale)
            || Math.Abs(scale) > MaxScale)
        {
            reason = "invalid scale";
            return false;
        }

        try
        {
            // scale="3" means the report is in thousands of kronor (tkr).
            var scaled = number * PowerOfTen(scale);
            if (scaled != decimal.Truncate(scaled))
            {
                reason = "not whole kronor";
                return false;
            }

            value = (long)scaled;
        }
        catch (OverflowException)
        {
            reason = "the value is too large";
            return false;
        }

        if ((string?)fact.Attribute("sign") == "-")
        {
            value = -value;
        }

        return true;
    }

    /// <summary>The text as a number >= 0, or null if it does not follow the format.</summary>
    private static decimal? ReadNumber(string text, string? format)
    {
        // "ixt:numspacecomma" -> "numspacecomma". The prefix differs between registry versions.
        var name = format?[(format.LastIndexOf(':') + 1)..];
        if (name is not null && ZeroFormats.Contains(name))
        {
            return 0m;
        }

        (string Thousands, char DecimalSign) rules;
        if (name is null)
        {
            rules = PlainNumber;
        }
        else if (!Formats.TryGetValue(name, out rules))
        {
            return null;
        }

        // \s also matches the no-break space (&#160;) and the narrow no-break space that
        // reports often put between the thousands. All of them become a plain space.
        text = Whitespace().Replace(text, " ").Trim();
        foreach (var separator in rules.Thousands)
        {
            text = text.Replace(separator.ToString(), "", StringComparison.Ordinal);
        }

        text = text.Replace(rules.DecimalSign, '.');

        // What is left must be a plain decimal number. A minus sign or a letter means
        // something is wrong, and then we don't guess.
        return PlainDecimal().IsMatch(text)
            && decimal.TryParse(
                text,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var number)
            ? number
            : null;
    }

    private static decimal PowerOfTen(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < Math.Abs(exponent); i++)
        {
            result *= 10m;
        }

        return exponent < 0 ? 1m / result : result;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)?$")]
    private static partial Regex PlainDecimal();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Document {DocumentId}: {Tag} ({ContextRef}) is skipped: {Reason}.")]
    private static partial void LogSkipped(
        ILogger logger,
        string documentId,
        string tag,
        string? contextRef,
        string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Document {DocumentId}: {Key} for {PeriodEnd} has different values, none is kept.")]
    private static partial void LogConflict(
        ILogger logger,
        string documentId,
        string key,
        DateOnly periodEnd);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document {DocumentId}: {Key} is missing.")]
    private static partial void LogMissing(ILogger logger, string documentId, string key);
}
