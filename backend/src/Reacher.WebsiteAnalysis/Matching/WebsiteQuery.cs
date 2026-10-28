using System.Text.RegularExpressions;

namespace Reacher.WebsiteAnalysis.Matching;

/// <summary>
/// Builds the Brave query for one company. T2-06 passes the result to
/// <c>IBraveSearchClient.SearchAsync</c>; this method does not call Brave.
/// </summary>
public static partial class WebsiteQuery
{
    /// <summary>
    /// Returns <c>"{name}" {city}</c> after stripping <c>AB</c>, <c>Aktiebolag</c>,
    /// and <c>(publ)</c>. The city is omitted when it is blank.
    /// </summary>
    public static string Build(string companyName, string? city)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);

        var stripped = LegalFormPattern().Replace(companyName, " ");
        var cleaned = WhitespacePattern().Replace(stripped, " ").Trim();
        if (cleaned.Length == 0)
        {
            throw new ArgumentException(
                "Company name must contain more than a legal form.",
                nameof(companyName));
        }

        var quoted = $"\"{cleaned}\"";
        if (string.IsNullOrWhiteSpace(city))
        {
            return quoted;
        }

        return $"{quoted} {city.Trim()}";
    }

    [GeneratedRegex(
        @"\s*(?:\(\s*publ\s*\)|\baktiebolag\b|\bab\b)\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegalFormPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
