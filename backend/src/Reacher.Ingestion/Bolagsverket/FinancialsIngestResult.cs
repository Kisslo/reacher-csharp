namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// Counts from one financials ingest run, for the log, T1-07 and the real-run numbers in a PR.
/// Counts only: never an orgnr, a name or an amount.
/// </summary>
/// <remarks>A record, so <c>ToString()</c> prints every count for the log line.</remarks>
public sealed record FinancialsIngestResult
{
    // Number of years stored -> number of companies. Sorted, so YearsStored reads 0, 1, 2, 3.
    private readonly SortedDictionary<int, int> _companiesByYears = new();

    public int Companies { get; internal set; }

    /// <summary>Bolagsverket has no digitally filed annual report for the company.</summary>
    public int CompaniesWithoutReports { get; internal set; }

    /// <summary>Something failed for the company. It is logged with the document id.</summary>
    public int CompaniesFailed { get; internal set; }

    public int ReportsDownloaded { get; internal set; }

    /// <summary>Not downloaded, because the report's own fiscal year was already stored.</summary>
    public int ReportsSkipped { get; internal set; }

    /// <summary>Rows added to or updated in <c>financial_fact</c>.</summary>
    public int FactsStored { get; internal set; }

    /// <summary>
    /// How many companies have 0, 1, 2 … of their latest fiscal years stored after the run,
    /// e.g. <c>0: 2, 2: 1, 3: 7</c>. Failed companies are not in it.
    /// </summary>
    public string YearsStored =>
        string.Join(", ", _companiesByYears.Select(pair => $"{pair.Key}: {pair.Value}"));

    public TimeSpan Duration { get; internal set; }

    internal void CountYears(int years) =>
        _companiesByYears[years] = _companiesByYears.GetValueOrDefault(years) + 1;
}
