using Reacher.Ingestion.Scb;

namespace Reacher.Ingestion.Tests.Scb;

/// <summary>
/// Made-up SCB rows for the ingest tests (hard rule 8). The orgnr are invented, and the
/// personnummer have month 13, so they can never belong to anyone.
/// </summary>
internal static class ScbTestData
{
    public const string Sni = "96210";
    public const string Stockholm = "0180";
    public const string Solna = "0184";

    // peOrgNr as SCB sends it: "16" + the 10-digit orgnr.
    public const string CompanyA = "165599001236";
    public const string CompanyB = "165599002234";
    public const string CompanyC = "165599003232";
    public const string CompanyD = "165599004230";

    public const string Personnummer = "198013012342";
    public const string OtherPersonnummer = "200113012341";

    /// <summary>12:00 Swedish time.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 11, 0, 0, TimeSpan.Zero);

    /// <summary>An eligible workplace in Stockholm, unless a parameter says otherwise.</summary>
    public static WorkplacePartial Workplace(
        string cfar,
        string peOrgNr,
        string municipality = Stockholm,
        string? aeStat = "1",
        string? reklamSparrTyp = "1",
        string? ben = " ") =>
        new(
            CfarNr: cfar,
            PeOrgNr: peOrgNr,
            Ben: ben,
            PrimarNaringsgren: new ScbIndustry(Sni, 1),
            Belagenhetsadress: new ScbVisitingAddress(
                municipality, " Påhittade gatan 1 ", "11850", "STOCKHOLM"),
            AeStat: aeStat,
            AnstKl: "2",
            ReklamSparrTyp: reklamSparrTyp);

    /// <summary>An eligible aktiebolag, unless a parameter says otherwise.</summary>
    public static LegalUnitFull LegalUnit(
        string peOrgNr,
        string? jurform = "49",
        string? ftgStat = "1",
        string? reklamSparrTyp = "1",
        string? foretagsnamn = "Påhittade Saxen AB",
        string? namn = "Påhittade Saxen AB",
        string? regDat = "2019-03-20T15:16:00.000Z") =>
        new(
            PeOrgNr: peOrgNr,
            Namn: namn,
            Foretagsnamn: foretagsnamn,
            Jurform: jurform,
            FtgStat: ftgStat,
            AnstKl: "3",
            OmsKl: "3",
            OmsAr: 2024,
            ReklamSparrTyp: reklamSparrTyp,
            RegDat: regDat,
            Naringsgrenar: [new ScbIndustry(Sni, 1)]);
}

/// <summary>A clock that stands still, so <c>updated_at</c> and <c>fetched_at</c> can be checked.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Keeps every report in order. <see cref="Progress{T}"/> would report later, on another
/// thread, so a test couldn't rely on it.
/// </summary>
internal sealed class RecordingProgress : IProgress<IngestProgress>
{
    public List<IngestProgress> Reports { get; } = [];

    public void Report(IngestProgress value) => Reports.Add(value);
}
