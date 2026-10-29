using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reacher.Data;
using Reacher.Data.Entities;
using Reacher.Ingestion.Options;

namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// Fills <c>financial_fact</c> with the figures in <c>Bolagsverket:TagMap</c> (D14) for
/// companies T1-05 has stored: the <see cref="BolagsverketOptions.Years"/> latest fiscal
/// years per company.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Reports are handled newest first, and a report is only downloaded if its own fiscal
/// year isn't stored yet. A report also holds the comparison year, so a first run downloads
/// about every other report, and a run with everything stored downloads nothing.</item>
/// <item>The newest report wins for a figure, because comparison figures get restated.</item>
/// <item>No report, or no figure for a year, gives no row. Never 0.</item>
/// <item>One failing company is logged with the document id, and the run goes on. Wrong
/// credentials stop the run, because every company would fail the same way.</item>
/// <item>The orgnr is never logged. Logs hold document ids and counts.</item>
/// </list>
/// </remarks>
public sealed partial class FinancialsIngestService(
    IBolagsverketClient bolagsverket,
    IxbrlParser parser,
    ReacherDbContext db,
    IOptions<BolagsverketOptions> options,
    TimeProvider time,
    ILogger<FinancialsIngestService> logger)
{
    public async Task<FinancialsIngestResult> RunAsync(
        IEnumerable<string> orgnrs,
        IProgress<IngestProgress> progress,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var now = time.GetUtcNow();
        var result = new FinancialsIngestResult();
        var tagMap = TagMap(options.Value);

        // A list, so the total is known for the progress before the first company.
        var companies = orgnrs.Distinct(StringComparer.Ordinal).ToList();
        progress.Report(new IngestProgress(IngestPhases.Financials, 0, companies.Count));

        for (var i = 0; i < companies.Count; i++)
        {
            await IngestCompanyAsync(companies[i], tagMap, now, result, cancellationToken);
            progress.Report(new IngestProgress(IngestPhases.Financials, i + 1, companies.Count));
        }

        result.Companies = companies.Count;
        result.Duration = time.GetElapsedTime(started);
        LogFinished(logger, result);
        return result;
    }

    /// <summary>
    /// One company, saved on its own. An error is logged and counted and never stops the run,
    /// except wrong credentials and cancellation.
    /// </summary>
    private async Task IngestCompanyAsync(
        string orgnr,
        IReadOnlyDictionary<string, string> tagMap,
        DateTimeOffset now,
        FinancialsIngestResult result,
        CancellationToken cancellationToken)
    {
        // What the error log names: the document being handled, or the list before it.
        string? documentId = null;
        try
        {
            // Tracked, so an update to a stored row is saved by SaveChangesAsync below.
            var stored = await db.FinancialFacts
                .Where(f => f.Orgnr == orgnr)
                .ToListAsync(cancellationToken);
            var storedYears = stored.Select(f => f.PeriodEnd).ToHashSet();

            var reports = Latest(await bolagsverket.GetDocuments(orgnr, cancellationToken));
            if (reports.Count == 0)
            {
                result.CompaniesWithoutReports++;
                result.CountYears(0);
                return;
            }

            // Fiscal year -> the figures from the newest report read in this run that has it.
            var found = new Dictionary<DateOnly, (string Document, List<FinancialValue> Values)>();

            foreach (var report in reports)
            {
                // Reports come newest first, so every report after this one is older still.
                if (!Window(reports, storedYears, found.Keys).Contains(report.PeriodEnd))
                {
                    break;
                }

                if (storedYears.Contains(report.PeriodEnd) || found.ContainsKey(report.PeriodEnd))
                {
                    result.ReportsSkipped++;
                    continue;
                }

                documentId = report.DocumentId;
                var values = await DownloadAndParseAsync(
                    report.DocumentId, tagMap, cancellationToken);
                result.ReportsDownloaded++;

                // The report's own year and its comparison year. TryAdd keeps a year that a
                // newer report already gave.
                foreach (var year in values.GroupBy(value => value.PeriodEnd))
                {
                    found.TryAdd(year.Key, (report.DocumentId, [.. year]));
                }
            }

            // Only the newest years are kept. The oldest comparison year often falls outside.
            var window = Window(reports, storedYears, found.Keys);
            var facts = 0;
            foreach (var (year, (fromDocument, values)) in found)
            {
                if (window.Contains(year))
                {
                    facts += Upsert(orgnr, values, fromDocument, stored, now);
                }
            }

            // One save per company: its rows are all written, or none are.
            await db.SaveChangesAsync(cancellationToken);
            result.FactsStored += facts;
            result.CountYears(
                window.Count(year => storedYears.Contains(year) || found.ContainsKey(year)));
        }
        catch (Exception exception) when (
            exception is not BolagsverketAuthException
            && !cancellationToken.IsCancellationRequested)
        {
            result.CompaniesFailed++;
            LogCompanyFailed(logger, documentId ?? "the document list", SafeReason(exception));
        }
        finally
        {
            // The context holds one company at a time. Rows that failed to save are dropped
            // here, or the next company's SaveChangesAsync would try them again.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// One report per fiscal year, newest year first. A year filed twice (a correction)
    /// gives the one registered last.
    /// </summary>
    private static List<AnnualReportDocument> Latest(
        IReadOnlyList<AnnualReportDocument> documents) =>
        [.. documents
            .GroupBy(document => document.PeriodEnd)
            // ISO text sorts as time. A missing timestamp ("") loses to one that is there.
            .Select(year => year.MaxBy(d => d.RegisteredAt, StringComparer.Ordinal)!)
            .OrderByDescending(document => document.PeriodEnd)];

    /// <summary>
    /// The <see cref="BolagsverketOptions.Years"/> newest fiscal years known for the company:
    /// from the document list, the database and the reports read so far. A company with one
    /// report on file still gets two years, because the report has a comparison year.
    /// </summary>
    private HashSet<DateOnly> Window(
        IEnumerable<AnnualReportDocument> reports,
        IEnumerable<DateOnly> storedYears,
        IEnumerable<DateOnly> foundYears) =>
        [.. reports
            .Select(report => report.PeriodEnd)
            .Concat(storedYears)
            .Concat(foundYears)
            .Distinct()
            .OrderDescending()
            .Take(options.Value.Years)];

    /// <summary>
    /// The figures in one document. A zip can hold more than one xhtml file. A figure two
    /// files disagree on is left out, as in the parser: a gap is better than a guess.
    /// </summary>
    private async Task<List<FinancialValue>> DownloadAndParseAsync(
        string documentId,
        IReadOnlyDictionary<string, string> tagMap,
        CancellationToken cancellationToken)
    {
        var files = await bolagsverket.Download(documentId, cancellationToken);
        var values = new List<FinancialValue>();
        try
        {
            foreach (var file in files)
            {
                values.AddRange(parser.Parse(file, tagMap, documentId));
            }
        }
        finally
        {
            foreach (var file in files)
            {
                await file.DisposeAsync();
            }
        }

        return [.. values
            .GroupBy(value => (value.PeriodEnd, value.Key))
            .Where(same => same.Select(value => value.Value).Distinct().Count() == 1)
            .Select(same => same.First())];
    }

    /// <summary>
    /// Adds each figure, or updates it if an older report stored it (the newest report wins).
    /// EF Core tracks one object per key, so a stored row is changed in place rather than
    /// removed and added again.
    /// </summary>
    /// <returns>How many rows were added or updated.</returns>
    private int Upsert(
        string orgnr,
        IEnumerable<FinancialValue> values,
        string documentId,
        List<FinancialFact> stored,
        DateTimeOffset now)
    {
        var count = 0;
        foreach (var value in values)
        {
            var existing = stored.Find(f => f.PeriodEnd == value.PeriodEnd && f.Key == value.Key);
            if (existing is null)
            {
                db.FinancialFacts.Add(new FinancialFact
                {
                    Orgnr = orgnr,
                    PeriodEnd = value.PeriodEnd,
                    Key = value.Key,
                    Value = value.Value,
                    DocumentId = documentId,
                    FetchedAt = now,
                });
            }
            else
            {
                existing.Value = value.Value;
                existing.DocumentId = documentId;
                existing.FetchedAt = now;
            }

            count++;
        }

        return count;
    }

    // Tag and Fact are validated at startup (BolagsverketOptions.Validate), so never blank.
    private static Dictionary<string, string> TagMap(BolagsverketOptions settings) =>
        settings.TagMap.ToDictionary(
            entry => entry.Tag!,
            entry => entry.Fact!,
            StringComparer.Ordinal);

    // Our own exceptions are written to be safe to log (route and document id only). Any
    // other message could quote data, e.g. a database error with the key values (the
    // orgnr), so only its type is logged (hard rule 6).
    private static string SafeReason(Exception exception) =>
        exception is BolagsverketException or IxbrlParseException
            ? exception.Message
            : exception.GetType().Name;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Financials for one company failed at {DocumentId}, it is skipped: {Reason}")]
    private static partial void LogCompanyFailed(ILogger logger, string documentId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Financials ingest: {Result}")]
    private static partial void LogFinished(ILogger logger, FinancialsIngestResult result);
}
