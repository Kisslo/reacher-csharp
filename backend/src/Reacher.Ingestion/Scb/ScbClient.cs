using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Reacher.Ingestion.Options;

namespace Reacher.Ingestion.Scb;

/// <summary>
/// Typed <see cref="HttpClient"/> for SCB's AFR API. Retries, timeouts, the API key
/// header and the base address are set up in <see cref="ScbServiceCollectionExtensions"/>.
/// </summary>
/// <remarks>
/// A peOrgNr is in the URL because SCB requires it there, so URLs are never logged:
/// the HttpClient loggers are removed, and errors name the route template only.
/// </remarks>
public sealed class ScbClient(HttpClient http, IOptions<ScbOptions> options, TimeProvider time)
    : IScbClient
{
    private readonly int _pageSize = options.Value.PageSize;

    public async IAsyncEnumerable<WorkplacePartial> GetWorkplacesBySni(
        string sni,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string route = "/arbetsstallen/naringsgren/{kod}";

        int? cursor = null;
        while (true)
        {
            // The same limit on every page (docs/scb-fields.md, "Paginering").
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"arbetsstallen/naringsgren/{Uri.EscapeDataString(sni)}?limit={_pageSize}");
            if (cursor is not null)
            {
                path += string.Create(CultureInfo.InvariantCulture, $"&cursorId={cursor}");
            }

            var page = await GetAsync<WorkplacePage>(path, route, cancellationToken)
                ?? throw new ScbException($"GET {route}: HTTP 404, unknown SNI code.");

            foreach (var workplace in page.Arbetsstallen ?? [])
            {
                yield return workplace;
            }

            var pagination = page.Pagination
                ?? throw new ScbException($"GET {route}: the response has no pagination.");
            if (!pagination.HasMore)
            {
                yield break;
            }

            // A missing or repeated cursor would loop forever.
            if (pagination.NextCursorId is not { } next || next == cursor)
            {
                throw new ScbException($"GET {route}: hasMore is true but the cursor is invalid.");
            }

            cursor = next;
        }
    }

    public Task<WorkplaceFull?> GetWorkplace(string cfarNr, CancellationToken cancellationToken) =>
        GetAsync<WorkplaceFull>(
            $"arbetsstallen/{Uri.EscapeDataString(cfarNr)}/full",
            "/arbetsstallen/{cfarNr}/full",
            cancellationToken);

    public Task<LegalUnitFull?> GetLegalUnit(string peOrgNr, CancellationToken cancellationToken)
    {
        // Fail closed (D2): only legal persons (16 + orgnr). A 19/20 prefix is a
        // personnummer and is never sent anywhere. The value is not in the message.
        if (!peOrgNr.StartsWith("16", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Only a peOrgNr that starts with 16 (a legal person) may be fetched.",
                nameof(peOrgNr));
        }

        return GetAsync<LegalUnitFull>(
            $"juridiskaenheter/{Uri.EscapeDataString(peOrgNr)}/full",
            "/juridiskaenheter/{peOrgNr}/full",
            cancellationToken);
    }

    public async Task<int> CountWorkplacesBySni(string sni, CancellationToken cancellationToken)
    {
        const string route = "/arbetsstallen/naringsgren/{kod}/count";
        var result = await GetAsync<ScbCount>(
            $"arbetsstallen/naringsgren/{Uri.EscapeDataString(sni)}/count",
            route,
            cancellationToken);
        return result?.Count ?? throw new ScbException($"GET {route}: HTTP 404, unknown SNI code.");
    }

    public async Task<IReadOnlyList<CodeItem>> GetCodeTable(
        string table,
        CancellationToken cancellationToken)
    {
        const string route = "/kodtabeller/{tabell}";
        var result = await GetAsync<List<CodeItem>>(
            $"kodtabeller/{Uri.EscapeDataString(table)}",
            route,
            cancellationToken);
        return result ?? throw new ScbException($"GET {route}: HTTP 404, unknown code table.");
    }

    /// <summary>JSON for 200, null for 404, an exception for anything else.</summary>
    /// <param name="path">Relative to the base address. May hold a peOrgNr: never log it.</param>
    /// <param name="route">The route template. The only thing errors show.</param>
    private async Task<T?> GetAsync<T>(string path, string route, CancellationToken cancellationToken)
        where T : class
    {
        // Checked before every call, so a long run stops with a clear error at 04:00
        // instead of retrying against a closed API.
        if (ScbMaintenanceWindow.Contains(time.GetUtcNow()))
        {
            throw new ScbMaintenanceWindowException();
        }

        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var status = (int)response.StatusCode;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ScbException($"GET {route}: HTTP {status}. Check the setting 'Scb:ApiKey'.");
        }

        if (!response.IsSuccessStatusCode)
        {
            // The body is not read into the message: problem+json can repeat the path.
            throw new ScbException($"GET {route}: HTTP {status}.");
        }

        return await response.Content.ReadFromJsonAsync<T>(ScbJson.Options, cancellationToken)
            ?? throw new ScbException($"GET {route}: the response body is empty.");
    }
}
