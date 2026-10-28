namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// One figure from an annual report. The financials ingest (T1-06) adds the orgnr and
/// the document id and stores it as a <c>financial_fact</c> row.
/// </summary>
/// <param name="PeriodEnd">The last day of the fiscal year, or the balance sheet date.</param>
/// <param name="Key">The fact name from <c>Bolagsverket:TagMap</c>, e.g. <c>revenue</c>.</param>
/// <param name="Value">Whole kronor. The sign is kept, so a loss is negative.</param>
public sealed record FinancialValue(DateOnly PeriodEnd, string Key, long Value);
