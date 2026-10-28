using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// Typed <see cref="HttpClient"/> for Bolagsverket's API. Retries, timeouts and the base
/// address are set up in <see cref="BolagsverketServiceCollectionExtensions"/>.
/// Ported from reacher's <c>bolagsverket.py</c>.
/// </summary>
/// <remarks>
/// The orgnr is only ever in a POST body, never in a URL, a log line or an error message.
/// Errors name the route and, for downloads, the document id.
/// </remarks>
internal sealed partial class BolagsverketClient(
    HttpClient http,
    BolagsverketTokenCache tokens,
    BolagsverketRateLimiter rateLimiter,
    ILogger<BolagsverketClient> logger) : IBolagsverketClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AnnualReportDocument>> GetDocuments(
        string orgnr,
        CancellationToken cancellationToken)
    {
        const string route = "POST /dokumentlista";

        // Fail closed (D2): only something that is an organisation number is sent. The
        // value is not in the message.
        if (!OrganisationNumber().IsMatch(orgnr))
        {
            throw new ArgumentException(
                "Only a 10-digit organisation number without a dash may be sent.",
                nameof(orgnr));
        }

        // POST, not GET, so the orgnr is in the body and never in a URL (D16).
        using var response = await SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                new Uri("dokumentlista", UriKind.Relative))
            {
                Content = JsonContent.Create(new DocumentListRequest(orgnr), options: Json),
            },
            route,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        var body = await response.Content.ReadFromJsonAsync<DocumentListResponse>(
            Json,
            cancellationToken)
            ?? throw new BolagsverketException($"{route}: the response body is empty.");

        var documents = new List<AnnualReportDocument>();
        foreach (var item in body.Dokument ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.DokumentId)
                || !TryReadDate(item.RapporteringsperiodTom, out var periodEnd))
            {
                LogInvalidEntry(logger);
                continue;
            }

            documents.Add(new AnnualReportDocument(
                item.DokumentId,
                periodEnd,
                item.Registreringstidpunkt ?? ""));
        }

        return documents;
    }

    public async Task<IReadOnlyList<Stream>> Download(
        string documentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        var route = $"GET /dokument/{documentId}";

        using var response = await SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                new Uri($"dokument/{Uri.EscapeDataString(documentId)}", UriKind.Relative)),
            route,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            LogNotFound(logger, documentId);
            return [];
        }

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return ReportFiles.Extract(payload, documentId, logger);
    }

    /// <summary>
    /// Sends with a token. Returns 200 and 404, throws for anything else. On a 401 it fetches
    /// one new token and sends again once.
    /// </summary>
    /// <param name="createRequest">A new request each time: one can only be sent once.</param>
    /// <param name="route">What errors show. Never contains the orgnr.</param>
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest,
        string route,
        CancellationToken cancellationToken)
    {
        string? rejected = null;
        while (true)
        {
            var token = await tokens.GetToken(rejected, cancellationToken);
            await rateLimiter.WaitAsync(cancellationToken);

            using var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await http.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound)
            {
                return response;
            }

            var status = (int)response.StatusCode;
            response.Dispose();

            // The token can stop working before expires_in, e.g. if it is revoked.
            if (status == (int)HttpStatusCode.Unauthorized && rejected is null)
            {
                rejected = token;
                continue;
            }

            // The body is not read into the message: it can repeat what we sent (the orgnr).
            if (status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
            {
                throw new BolagsverketAuthException(
                    $"{route}: HTTP {status}. Check the settings 'Bolagsverket:ClientId' and "
                    + "'Bolagsverket:ClientSecret', and that the app has the API in the portal.");
            }

            throw new BolagsverketException($"{route}: HTTP {status}.");
        }
    }

    /// <summary>yyyy-MM-dd, also when a time follows (2026-04-30T00:00:00).</summary>
    private static bool TryReadDate(string? text, out DateOnly date)
    {
        date = default;
        return text is { Length: >= 10 }
            && DateOnly.TryParseExact(
                text[..10],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
    }

    // 10 digits, the third one 2-9. In a personnummer (YYMMDDNNNN) the third digit is the
    // first digit of the month, 0 or 1, so a personnummer can never match.
    [GeneratedRegex("^[0-9]{2}[2-9][0-9]{7}$")]
    private static partial Regex OrganisationNumber();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The document list has an entry without dokumentId or period, it is skipped.")]
    private static partial void LogInvalidEntry(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Document {DocumentId}: Bolagsverket does not have it, it is skipped.")]
    private static partial void LogNotFound(ILogger logger, string documentId);
}
