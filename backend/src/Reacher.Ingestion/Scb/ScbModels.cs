using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reacher.Ingestion.Scb;

// The shapes of SCB's JSON, with SCB's own field names (docs/scb-fields.md), so the
// mapping in T1-05 can be checked field by field. Only fields we use are mapped.
// postAdress, kommunSate, tel, epost and coordinates are left out on purpose (D3,
// data minimisation); the serializer ignores fields that have no property.
// Values are raw: "" and " " stay as they are. The ingest trims and normalises.

/// <summary>One page of <c>GET /arbetsstallen/naringsgren/{kod}</c>.</summary>
public sealed record WorkplacePage(
    ScbPagination? Pagination,
    IReadOnlyList<WorkplacePartial>? Arbetsstallen);

/// <summary>Cursor paging. The end is decided by <see cref="HasMore"/> only.</summary>
public sealed record ScbPagination(int? NextCursorId, int Limit, bool HasMore);

/// <summary>A workplace (AE) from the list endpoint.</summary>
public sealed record WorkplacePartial(
    [property: JsonConverter(typeof(ScbCodeConverter))] string? CfarNr,
    string? PeOrgNr,
    string? Ben,
    ScbIndustry? PrimarNaringsgren,
    ScbVisitingAddress? Belagenhetsadress,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? AeStat,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? AnstKl,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? ReklamSparrTyp);

/// <summary>A workplace (AE) from <c>GET /arbetsstallen/{cfarNr}/full</c>.</summary>
public sealed record WorkplaceFull(
    [property: JsonConverter(typeof(ScbCodeConverter))] string? CfarNr,
    string? PeOrgNr,
    string? Ben,
    ScbVisitingAddress? Belagenhetsadress,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? AeStat,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? AnstKl,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? ReklamSparrTyp,
    string? StartDat,
    IReadOnlyList<ScbIndustry>? Naringsgrenar);

/// <summary>A legal unit (JE) from <c>GET /juridiskaenheter/{peOrgNr}/full</c>.</summary>
/// <remarks>Dates stay as SCB's UTC strings. The ingest converts them to Swedish dates.</remarks>
public sealed record LegalUnitFull(
    string? PeOrgNr,
    string? Namn,
    string? Foretagsnamn,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? Jurform,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? FtgStat,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? AnstKl,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? OmsKl,
    int? OmsAr,
    [property: JsonConverter(typeof(ScbCodeConverter))] string? ReklamSparrTyp,
    string? RegDat,
    IReadOnlyList<ScbIndustry>? Naringsgrenar);

/// <summary>The visiting address. Never <c>postAdress</c> (D3).</summary>
public sealed record ScbVisitingAddress(
    [property: JsonConverter(typeof(ScbCodeConverter))] string? Kommun,
    string? BGatuAdress,
    string? BPostNr,
    string? BPostOrt);

/// <summary>An SNI code. <see cref="Rangordning"/> 1 is the primary one (D4).</summary>
public sealed record ScbIndustry(
    [property: JsonConverter(typeof(ScbCodeConverter))] string? Naringsgren,
    int? Rangordning);

/// <summary>A row of <c>GET /kodtabeller/{tabell}</c>.</summary>
public sealed record CodeItem(
    [property: JsonConverter(typeof(ScbCodeConverter))] string? Kod,
    string? Klartext);

/// <summary>The answer of <c>GET /arbetsstallen/naringsgren/{kod}/count</c>.</summary>
public sealed record ScbCount(int Count);

internal static class ScbJson
{
    /// <summary>camelCase and case-insensitive names, the same defaults ASP.NET Core uses.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
