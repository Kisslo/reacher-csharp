namespace Reacher.Ingestion.Scb;

/// <summary>
/// Counts from one SCB ingest run, for the log, T1-07 and the real-run numbers in a PR.
/// Counts only: never an orgnr, a name or an address.
/// </summary>
/// <remarks>A record, so <c>ToString()</c> prints every count for the log line.</remarks>
public sealed record ScbIngestResult
{
    /// <summary>Rows in SCB's list for the SNI code, for the whole country.</summary>
    public int WorkplacesRead { get; internal set; }

    /// <summary>Dropped first: not "16" + an orgnr, e.g. a personnummer (D2).</summary>
    public int NotLegalPerson { get; internal set; }

    /// <summary>The workplace is in a municipality that wasn't asked for (D3).</summary>
    public int OtherMunicipality { get; internal set; }

    /// <summary><c>aeStat</c> or <c>reklamSparrTyp</c> not in the allow-list (D5).</summary>
    public int WorkplaceNotEligible { get; internal set; }

    public int CompaniesFetched { get; internal set; }

    /// <summary>SCB answered 404 for the company.</summary>
    public int CompanyNotFound { get; internal set; }

    /// <summary><c>jurform</c>, <c>ftgStat</c> or <c>reklamSparrTyp</c> not in the allow-list (D5).</summary>
    public int CompanyNotEligible { get; internal set; }

    /// <summary>No name, employee class or revenue class, which the schema requires.</summary>
    public int CompanyIncomplete { get; internal set; }

    public int CompaniesStored { get; internal set; }

    public int WorkplacesStored { get; internal set; }

    public int WorkplacesDeleted { get; internal set; }

    public int CompaniesDeleted { get; internal set; }

    public TimeSpan Duration { get; internal set; }
}
