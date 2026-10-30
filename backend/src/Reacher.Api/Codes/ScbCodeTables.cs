using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Reacher.Ingestion.Scb;

namespace Reacher.Api.Codes;

/// <summary>
/// Reads SCB's code tables and keeps each one in memory for 24 hours. The lists are public
/// reference data, not Brave results, so they may sit in memory.
/// </summary>
public sealed class ScbCodeTables(IScbClient scb, IMemoryCache cache) : ICodeTables
{
    public const string MunicipalityTableName = "kommunkoder";

    public const string EmployeeClassTableName = "anstklkoder";

    public const string RevenueClassTableName = "omsklkoder";

    public const string SniTableName = "naringsgrenkoder";

    private const int MunicipalityWidth = 4;

    private const string FirstMunicipality = "0100";

    private const string LastMunicipality = "2599";

    private const int SniWidth = 5;

    /// <summary>Class codes are compared with the raw codes on a company, so never padded.</summary>
    private const int NoPadding = 0;

    /// <summary>The code lists change rarely, so a day is enough.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    private static readonly StringComparer SwedishOrder = StringComparer.Create(
        CultureInfo.GetCultureInfo("sv-SE"),
        ignoreCase: false);

    private static string CacheKey(string table) => "reacher.code-table:" + table;

    public Task<IReadOnlyList<CodeResponse>> GetMunicipalitiesAsync(
        CancellationToken cancellationToken) =>
        LoadAsync(MunicipalityTableName, MunicipalityWidth, RealMunicipalities, cancellationToken);

    public Task<IReadOnlyList<CodeResponse>> GetEmployeeClassesAsync(
        CancellationToken cancellationToken) =>
        LoadAsync(EmployeeClassTableName, NoPadding, ByCode, cancellationToken);

    public Task<IReadOnlyList<CodeResponse>> GetRevenueClassesAsync(
        CancellationToken cancellationToken) =>
        LoadAsync(RevenueClassTableName, NoPadding, ByCode, cancellationToken);

    public async Task<CodeResponse?> FindSniAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var rows = await LoadAsync(SniTableName, SniWidth, ByCode, cancellationToken);
        return rows.FirstOrDefault(row => row.Code == code);
    }

    private async Task<IReadOnlyList<CodeResponse>> LoadAsync(
        string table,
        int width,
        Func<IEnumerable<CodeResponse>, IEnumerable<CodeResponse>> arrange,
        CancellationToken cancellationToken)
    {
        // A failed call throws and is not stored, so the next request tries again.
        var rows = await cache.GetOrCreateAsync(CacheKey(table), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return await ReadAsync(table, width, arrange, cancellationToken);
        });

        return rows
            ?? throw new ScbException("GET /kodtabeller/{tabell}: the code table was empty.");
    }

    private async Task<IReadOnlyList<CodeResponse>> ReadAsync(
        string table,
        int width,
        Func<IEnumerable<CodeResponse>, IEnumerable<CodeResponse>> arrange,
        CancellationToken cancellationToken)
    {
        var rows = await scb.GetCodeTable(table, cancellationToken);
        var codes = rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Kod)
                && !string.IsNullOrWhiteSpace(row.Klartext))
            .Select(row => new CodeResponse(Normalize(row.Kod!, width), row.Klartext!.Trim()));
        var arranged = arrange(codes).ToList();

        if (arranged.Count == 0)
        {
            throw new ScbException("GET /kodtabeller/{tabell}: the code table was empty.");
        }

        return arranged;
    }

    private static IEnumerable<CodeResponse> RealMunicipalities(IEnumerable<CodeResponse> rows) =>
        rows.Where(IsMunicipality).OrderBy(static row => row.Name, SwedishOrder);

    /// <summary>
    /// SCB's table also has <c>0000</c> (code missing) and <c>9999</c> (not a Swedish
    /// municipality). Only counties 01 to 25 count, so a new placeholder stays out too.
    /// </summary>
    private static bool IsMunicipality(CodeResponse row) =>
        row.Code.Length == MunicipalityWidth
        && row.Code.All(char.IsAsciiDigit)
        && string.CompareOrdinal(row.Code, FirstMunicipality) >= 0
        && string.CompareOrdinal(row.Code, LastMunicipality) <= 0;

    /// <summary>A shorter digit string is a smaller number, so <c>2</c> sorts before <c>10</c>.</summary>
    private static IEnumerable<CodeResponse> ByCode(IEnumerable<CodeResponse> rows) =>
        rows.OrderBy(static row => row.Code.Length)
            .ThenBy(static row => row.Code, StringComparer.Ordinal);

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
