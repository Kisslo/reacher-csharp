using System.Text.Json.Serialization;

namespace Reacher.Ingestion.Bolagsverket;

/// <summary>One annual report in Bolagsverket's document list.</summary>
/// <param name="DocumentId">Bolagsverket's id. Safe to log, unlike the orgnr.</param>
/// <param name="PeriodEnd">The last day of the fiscal year (<c>rapporteringsperiodTom</c>).</param>
/// <param name="RegisteredAt">
/// <c>registreringstidpunkt</c> as Bolagsverket sends it (ISO text, so it sorts). If a year
/// was filed twice (a correction), T1-06 uses this to pick the latest one.
/// </param>
public sealed record AnnualReportDocument(
    string DocumentId,
    DateOnly PeriodEnd,
    string RegisteredAt);

// The shapes of Bolagsverket's JSON, with Bolagsverket's own field names.

/// <summary>The body of <c>POST /dokumentlista</c>.</summary>
internal sealed record DocumentListRequest(string Identitetsbeteckning);

/// <summary>The answer of <c>POST /dokumentlista</c>.</summary>
internal sealed record DocumentListResponse(IReadOnlyList<DocumentListItem>? Dokument);

internal sealed record DocumentListItem(
    string? DokumentId,
    string? RapporteringsperiodTom,
    string? Registreringstidpunkt);

/// <summary>The OAuth2 token answer. OAuth2 uses snake_case, so the names are explicit.</summary>
internal sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn);
