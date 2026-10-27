using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Reads the quick checks from HTML. Copyright years are taken from the footer
/// when a footer element exists, and from the body otherwise. A year counts only
/// when it sits within 40 characters of © or the word "copyright".
/// </summary>
public static partial class HtmlPageChecks
{
    private const int CopyrightWindow = 40;

    private const int OldestCopyrightYear = 1995;

    public static HtmlFindings Read(string html, Uri finalUrl, TimeProvider time)
    {
        using var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.QuerySelectorAll("script, style, noscript").ToList())
        {
            element.Parent?.RemoveChild(element);
        }

        var viewport = false;
        string? generator = null;
        foreach (var meta in document.QuerySelectorAll("meta"))
        {
            var name = meta.GetAttribute("name");
            if (name is null)
            {
                continue;
            }

            if (name.Equals("viewport", StringComparison.OrdinalIgnoreCase))
            {
                viewport = true;
            }
            else if (generator is null && name.Equals("generator", StringComparison.OrdinalIgnoreCase))
            {
                var content = meta.GetAttribute("content");
                generator = string.IsNullOrWhiteSpace(content) ? null : content.Trim();
            }
        }

        var footer = document.QuerySelector("footer");
        var text = footer is not null
            ? footer.TextContent
            : document.Body?.TextContent ?? "";
        var copyrightYear = HighestCopyrightYear(text, StockholmClock.Year(time));
        var https = finalUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal);
        return new HtmlFindings(https, viewport, generator, copyrightYear);
    }

    private static int? HighestCopyrightYear(string text, int currentYear)
    {
        int? highest = null;
        foreach (Match marker in CopyrightMarker().Matches(text))
        {
            var start = Math.Max(0, marker.Index - CopyrightWindow);
            var end = Math.Min(text.Length, marker.Index + marker.Length + CopyrightWindow);
            foreach (Match yearMatch in Year().Matches(text[start..end]))
            {
                if (!int.TryParse(
                    yearMatch.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var year))
                {
                    continue;
                }

                if (year < OldestCopyrightYear || year > currentYear)
                {
                    continue;
                }

                if (highest is null || year > highest)
                {
                    highest = year;
                }
            }
        }

        return highest;
    }

    [GeneratedRegex(
        @"©|\bcopyright\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CopyrightMarker();

    [GeneratedRegex(@"\b\d{4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Year();
}
