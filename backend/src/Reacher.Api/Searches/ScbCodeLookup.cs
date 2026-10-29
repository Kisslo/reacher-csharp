using Microsoft.Extensions.Caching.Memory;
using Reacher.Ingestion.Scb;

namespace Reacher.Api.Searches;

/// <summary>
/// Checks codes against SCB's <c>naringsgrenkoder</c> and <c>kommunkoder</c> tables.
/// The lists are public reference data, not Brave results, so they may sit in memory.
/// Cached for 24 hours, the same lifetime T1-08 uses for <c>/api/codes</c>.
/// </summary>
public sealed class ScbCodeLookup(IScbClient scb, IMemoryCache cache) : ICodeLookup
{
    public const string SniTableName = "naringsgrenkoder";

    public const string MunicipalityTableName = "kommunkoder";

    private const int SniWidth = 5;

    private const int MunicipalityWidth = 4;

    /// <summary>Matches T1-08: the code lists change rarely, so a day is enough.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    private static string CacheKey(string table) => "reacher.search-codes:" + table;

    public async Task<bool> IsKnownSniAsync(string sni, CancellationToken cancellationToken)
    {
        var known = await LoadAsync(SniTableName, SniWidth, cancellationToken);
        return known.Contains(Normalize(sni, SniWidth));
    }

    public async Task<IReadOnlyList<string>> UnknownMunicipalitiesAsync(
        IReadOnlyCollection<string> municipalities,
        CancellationToken cancellationToken)
    {
        var known = await LoadAsync(MunicipalityTableName, MunicipalityWidth, cancellationToken);
        return municipalities
            .Where(code => !known.Contains(Normalize(code, MunicipalityWidth)))
            .ToList();
    }

    private async Task<IReadOnlySet<string>> LoadAsync(
        string table,
        int width,
        CancellationToken cancellationToken)
    {
        // A failed call throws and is not stored, so the next search tries again.
        var codes = await cache.GetOrCreateAsync(CacheKey(table), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return await ReadAsync(table, width, cancellationToken);
        });

        return codes
            ?? throw new ScbException("GET /kodtabeller/{tabell}: the code table was empty.");
    }

    private async Task<HashSet<string>> ReadAsync(
        string table,
        int width,
        CancellationToken cancellationToken)
    {
        var rows = await scb.GetCodeTable(table, cancellationToken);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Kod))
            {
                continue;
            }

            codes.Add(Normalize(row.Kod, width));
        }

        if (codes.Count == 0)
        {
            throw new ScbException("GET /kodtabeller/{tabell}: the code table was empty.");
        }

        return codes;
    }

    /// <summary>
    /// SCB sometimes sends a code as a JSON number, which drops a leading zero (D12).
    /// <c>180</c> and <c>0180</c> are the same municipality. A dot in an SNI code is ignored.
    /// </summary>
    private static string Normalize(string code, int width)
    {
        var trimmed = code.Trim().Replace(".", "", StringComparison.Ordinal);
        if (trimmed.Length > 0
            && trimmed.Length < width
            && trimmed.All(static digit => char.IsAsciiDigit(digit)))
        {
            return trimmed.PadLeft(width, '0');
        }

        return trimmed;
    }
}
