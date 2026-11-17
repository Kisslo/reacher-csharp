using System.Text;
using System.Text.RegularExpressions;

namespace Reacher.WebsiteAnalysis.Matching;

/// <summary>
/// Builds the Brave query for one company. The website endpoints pass the result to
/// <c>IBraveSearchClient.SearchAsync</c>; this method does not call Brave.
/// </summary>
public static partial class WebsiteQuery
{
    /// <summary>
    /// Returns <c>"{name}" {city} website loc:se -site:…</c> after stripping <c>AB</c>,
    /// <c>Aktiebolag</c>, and <c>(publ)</c>. The city and website term are omitted when
    /// the city is blank. <c>loc:se</c> is always added (ISO 3166-1 alpha-2 for Sweden).
    /// Each non-blank <paramref name="blockedDomains"/> entry becomes <c>-site:{domain}</c>.
    /// </summary>
    public static string Build(
        string companyName,
        string? city,
        IEnumerable<string>? blockedDomains = null)
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

        var query = new StringBuilder();
        query.Append('"').Append(cleaned).Append('"');

        if (!string.IsNullOrWhiteSpace(city))
        {
            query.Append(' ').Append(city.Trim()).Append(" website");
        }

        query.Append(" loc:se");

        if (blockedDomains is not null)
        {
            foreach (var domain in blockedDomains)
            {
                if (string.IsNullOrWhiteSpace(domain))
                {
                    continue;
                }

                var host = domain.Trim().TrimEnd('.').ToLowerInvariant();
                if (host.Length == 0)
                {
                    continue;
                }

                query.Append(" -site:").Append(host);
            }
        }

        return query.ToString();
    }

    [GeneratedRegex(
        @"\s*(?:\(\s*publ\s*\)|\baktiebolag\b|\bab\b)\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegalFormPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
