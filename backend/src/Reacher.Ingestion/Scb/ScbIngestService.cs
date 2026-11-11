using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reacher.Data;
using Reacher.Data.Entities;
using Reacher.Ingestion.Options;

namespace Reacher.Ingestion.Scb;

/// <summary>
/// Fills <c>company</c>, <c>workplace</c> and <c>coverage</c> for one SNI code and one or
/// more municipalities. SCB has no combined filter, so the SNI code is listed for the whole
/// country and filtered by the workplace's municipality here (docs/scb-fields.md).
/// </summary>
/// <remarks>
/// <para>
/// Only eligible aktiebolag are stored (D2, D5). A personnummer is dropped as the very first
/// step: it is never mapped, fetched, logged or stored. Logs hold counts only.
/// </para>
/// <para>
/// Two calls (D19). <see cref="RunAsync"/> commits companies in batches while it fetches them,
/// so the company list fills up during a long search. <see cref="CompleteAsync"/> marks the
/// data fresh and deletes what SCB no longer lists, and is only called once the whole job has
/// succeeded. A failed or cancelled job keeps its batches and never runs that cleanup.
/// </para>
/// </remarks>
public sealed partial class ScbIngestService(
    IScbClient scb,
    ReacherDbContext db,
    IOptions<ScbOptions> scbOptions,
    IOptions<IngestOptions> ingestOptions,
    TimeProvider time,
    ILogger<ScbIngestService> logger)
{
    /// <summary>SCB's prefix for a legal person: <c>16</c> + the 10-digit orgnr.</summary>
    private const string LegalPersonPrefix = "16";

    // Allow-lists (D5), never "!= '9'". A new or missing code isn't in the list, so the row
    // is left out (fail closed). Legal forms come from Ingest:AllowedLegalForms.
    private static readonly string[] ActiveStatuses = ["1"];
    private static readonly string[] AcceptsAdvertising = ["1"];

    private static readonly TimeZoneInfo Stockholm =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    /// <summary>
    /// Steps 1-5: reads SCB and commits every <see cref="IngestOptions.SaveBatchSize"/>
    /// companies. Coverage and the cleanup are left to <see cref="CompleteAsync"/>.
    /// </summary>
    public async Task<ScbIngestRun> RunAsync(
        string sni,
        IReadOnlyCollection<string> municipalities,
        IProgress<IngestProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sni);
        if (municipalities.Count == 0)
        {
            throw new ArgumentException(
                "At least one municipality is required.",
                nameof(municipalities));
        }

        var started = time.GetTimestamp();
        var run = new ScbIngestRun(sni, municipalities, time.GetUtcNow());

        var byCompany = await ReadWorkplacesAsync(run, progress, cancellationToken);
        await FetchAndSaveCompaniesAsync(run, byCompany, progress, cancellationToken);

        run.Result.Duration = time.GetElapsedTime(started);
        LogSaved(logger, sni, run.Municipalities.Count, run.Result);
        return run;
    }

    /// <summary>
    /// Steps 6-7 in one transaction, for a job that has succeeded (D19). Never call this for
    /// a job that failed or was cancelled: the run has only seen part of SCB's list, so the
    /// cleanup would delete companies that are still listed, and their outreach with them
    /// (D18).
    /// </summary>
    public async Task CompleteAsync(ScbIngestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await UpsertCoverageAsync(run, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // Step 6: what SCB no longer lists in the requested municipalities is gone. Other
        // municipalities belong to other searches and are left alone.
        var municipalities = run.Municipalities.ToList();
        var seen = run.Cfars.ToList();
        run.Result.WorkplacesDeleted = await db.Workplaces
            .Where(w => w.Sni == run.Sni
                && municipalities.Contains(w.Municipality)
                && !seen.Contains(w.Cfar))
            .ExecuteDeleteAsync(cancellationToken);

        // A company is only shown through its workplaces (D15), so one without any goes too.
        run.Result.CompaniesDeleted += await db.Companies
            .Where(c => !db.Workplaces.Any(w => w.Orgnr == c.Orgnr))
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        LogCompleted(logger, run.Sni, run.Municipalities.Count, run.Result);
    }

    /// <summary>
    /// Steps 1-3: walks every page and keeps the workplaces that may be stored, by orgnr.
    /// </summary>
    private async Task<Dictionary<string, List<Workplace>>> ReadWorkplacesAsync(
        ScbIngestRun run,
        IProgress<IngestProgress> progress,
        CancellationToken cancellationToken)
    {
        var sni = run.Sni;
        var result = run.Result;
        var pageSize = scbOptions.Value.PageSize;
        var byCompany = new Dictionary<string, List<Workplace>>(StringComparer.Ordinal);
        var seenCfars = new HashSet<string>(StringComparer.Ordinal);
        progress.Report(new IngestProgress(IngestPhases.Workplaces, 0, null));

        await foreach (var row in scb.GetWorkplacesBySni(sni, cancellationToken))
        {
            result.WorkplacesRead++;

            // The client asks for the same limit on every page, so every page but the last
            // is full, and this counts pages.
            if (result.WorkplacesRead % pageSize == 0)
            {
                progress.Report(new IngestProgress(
                    IngestPhases.Workplaces, result.WorkplacesRead / pageSize, null));
            }

            // D2, before anything else: only "16" + an orgnr goes on. A 19/20 prefix is a
            // personnummer. It is counted, and never mapped, kept, fetched or logged.
            if (!TryGetOrgnr(row.PeOrgNr, out var orgnr))
            {
                result.NotLegalPerson++;
                continue;
            }

            // D3: the workplace's own municipality, never the company's kommunSate.
            if (row.Belagenhetsadress is not { Kommun: { } municipality } address
                || !run.Municipalities.Contains(municipality))
            {
                result.OtherMunicipality++;
                continue;
            }

            if (Clean(row.CfarNr) is not { } cfar
                || row.PrimarNaringsgren?.Naringsgren != sni
                || !IsIn(row.AeStat, ActiveStatuses)
                || !IsIn(row.ReklamSparrTyp, AcceptsAdvertising))
            {
                result.WorkplaceNotEligible++;
                continue;
            }

            // A row repeated across pages would be added twice and fail the save.
            if (!seenCfars.Add(cfar))
            {
                continue;
            }

            if (!byCompany.TryGetValue(orgnr, out var companyWorkplaces))
            {
                companyWorkplaces = [];
                byCompany[orgnr] = companyWorkplaces;
            }

            companyWorkplaces.Add(new Workplace
            {
                Cfar = cfar,
                Orgnr = orgnr,
                Name = Clean(row.Ben),
                Sni = sni,
                Municipality = municipality,
                Street = Clean(address.BGatuAdress) ?? "",
                PostalCode = Clean(address.BPostNr) ?? "",
                City = Clean(address.BPostOrt) ?? "",
                WorkplaceStatus = row.AeStat,
                AdBlockType = row.ReklamSparrTyp,
                UpdatedAt = run.StartedAt,
            });
        }

        var pages = (result.WorkplacesRead + pageSize - 1) / pageSize;
        progress.Report(new IngestProgress(IngestPhases.Workplaces, pages, pages));
        return byCompany;
    }

    /// <summary>
    /// Steps 4-5: one call per company, however many workplaces it has (the cache per run),
    /// and a commit every <see cref="IngestOptions.SaveBatchSize"/> companies.
    /// </summary>
    private async Task FetchAndSaveCompaniesAsync(
        ScbIngestRun run,
        Dictionary<string, List<Workplace>> byCompany,
        IProgress<IngestProgress> progress,
        CancellationToken cancellationToken)
    {
        var batchSize = ingestOptions.Value.SaveBatchSize;
        var batch = new SaveBatch();
        var done = 0;
        progress.Report(new IngestProgress(IngestPhases.Companies, 0, byCompany.Count));

        foreach (var (orgnr, companyWorkplaces) in byCompany)
        {
            var unit = await scb.GetLegalUnit(LegalPersonPrefix + orgnr, cancellationToken);
            run.Result.CompaniesFetched++;
            if (unit is null)
            {
                run.Result.CompanyNotFound++;
            }

            if (unit is not null
                && ToEligibleCompany(orgnr, unit, run.Sni, run.StartedAt, run.Result) is { } company)
            {
                batch.Companies.Add(company);
                batch.Workplaces.AddRange(companyWorkplaces);
            }
            else
            {
                // Fail closed: also removes the company if an earlier run stored it.
                batch.NotEligible.Add(orgnr);
            }

            done++;
            if (batch.Count >= batchSize || done == byCompany.Count)
            {
                await SaveBatchAsync(run, batch, cancellationToken);
                batch = new SaveBatch();
            }

            progress.Report(new IngestProgress(IngestPhases.Companies, done, byCompany.Count));
        }
    }

    /// <summary>The company to store, or null (and a count) if it may not be stored.</summary>
    private Company? ToEligibleCompany(
        string orgnr,
        LegalUnitFull unit,
        string sni,
        DateTimeOffset now,
        ScbIngestResult result)
    {
        if (!IsIn(unit.Jurform, ingestOptions.Value.AllowedLegalForms)
            || !IsIn(unit.FtgStat, ActiveStatuses)
            || !IsIn(unit.ReklamSparrTyp, AcceptsAdvertising))
        {
            result.CompanyNotEligible++;
            return null;
        }

        // These columns are NOT NULL in the contract, so a company without them can't be
        // stored. It is left out and counted, never filled with a guess.
        var name = Clean(unit.Foretagsnamn) ?? Clean(unit.Namn);
        if (name is null
            || unit.AnstKl is null
            || unit.OmsKl is null
            || unit.OmsAr is not { } revenueClassYear)
        {
            result.CompanyIncomplete++;
            return null;
        }

        return new Company
        {
            Orgnr = orgnr,
            Name = name,
            Sni = unit.Naringsgrenar?.FirstOrDefault(i => i.Rangordning == 1)?.Naringsgren ?? sni,
            LegalForm = unit.Jurform,
            CompanyStatus = unit.FtgStat,
            EmployeeClass = unit.AnstKl,
            RevenueClass = unit.OmsKl,
            RevenueClassYear = revenueClassYear,
            RegisteredAt = ToSwedishDate(unit.RegDat),
            AdBlockType = unit.ReklamSparrTyp,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Step 5: one batch in one transaction. A batch is either saved whole or not at all, and
    /// once committed it stays, also if the job fails later (D19).
    /// </summary>
    private async Task SaveBatchAsync(
        ScbIngestRun run,
        SaveBatch batch,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // A company SCB says may no longer be shown goes at once, not at the end (fail
        // closed): with its workplaces in other municipalities, its financial facts and its
        // outreach (ON DELETE CASCADE).
        var notEligible = batch.NotEligible;
        if (notEligible.Count > 0)
        {
            run.Result.CompaniesDeleted += await db.Companies
                .Where(c => notEligible.Contains(c.Orgnr))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await UpsertCompaniesAsync(batch.Companies, cancellationToken);
        await UpsertWorkplacesAsync(batch.Workplaces, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The rows are saved, so there is no reason to keep tracking them. Without this the
        // DbContext would hold every company of a large search until the job ends.
        db.ChangeTracker.Clear();

        run.Result.CompaniesStored += batch.Companies.Count;
        run.Result.WorkplacesStored += batch.Workplaces.Count;
        run.Orgnrs.UnionWith(batch.Companies.Select(c => c.Orgnr));
        run.Cfars.UnionWith(batch.Workplaces.Select(w => w.Cfar));
    }

    private async Task UpsertCompaniesAsync(
        List<Company> companies,
        CancellationToken cancellationToken)
    {
        var orgnrs = companies.Select(c => c.Orgnr).ToList();
        var stored = await db.Companies
            .Where(c => orgnrs.Contains(c.Orgnr))
            .ToDictionaryAsync(c => c.Orgnr, cancellationToken);

        foreach (var company in companies)
        {
            if (stored.TryGetValue(company.Orgnr, out var existing))
            {
                // Copies every column by name onto the tracked row.
                db.Entry(existing).CurrentValues.SetValues(company);
            }
            else
            {
                db.Companies.Add(company);
            }
        }
    }

    private async Task UpsertWorkplacesAsync(
        List<Workplace> workplaces,
        CancellationToken cancellationToken)
    {
        var cfars = workplaces.Select(w => w.Cfar).ToList();
        var stored = await db.Workplaces
            .Where(w => cfars.Contains(w.Cfar))
            .ToDictionaryAsync(w => w.Cfar, cancellationToken);

        foreach (var workplace in workplaces)
        {
            if (stored.TryGetValue(workplace.Cfar, out var existing))
            {
                db.Entry(existing).CurrentValues.SetValues(workplace);
            }
            else
            {
                db.Workplaces.Add(workplace);
            }
        }
    }

    /// <summary>
    /// Step 7: every requested municipality is fresh now, also one where nothing matched,
    /// so the next search there doesn't start a new ingest (D6). The time is when the run
    /// started, because that is how old the data is.
    /// </summary>
    private async Task UpsertCoverageAsync(
        ScbIngestRun run,
        CancellationToken cancellationToken)
    {
        foreach (var municipality in run.Municipalities)
        {
            var coverage = await db.Coverage.FindAsync([run.Sni, municipality], cancellationToken);
            if (coverage is null)
            {
                db.Coverage.Add(new Coverage
                {
                    Sni = run.Sni,
                    Municipality = municipality,
                    FetchedAt = run.StartedAt,
                });
            }
            else
            {
                coverage.FetchedAt = run.StartedAt;
            }
        }
    }

    /// <summary>
    /// "16" + a 10-digit organisation number gives the orgnr. Anything else, such as a
    /// personnummer (19/20 prefix), gives false.
    /// </summary>
    private static bool TryGetOrgnr(string? peOrgNr, [NotNullWhen(true)] out string? orgnr)
    {
        orgnr = null;
        if (peOrgNr is null || !peOrgNr.StartsWith(LegalPersonPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = peOrgNr[LegalPersonPrefix.Length..];
        if (!OrganisationNumber().IsMatch(candidate))
        {
            return false;
        }

        orgnr = candidate;
        return true;
    }

    /// <summary>True only for a code in the allow-list. Null is never in it (fail closed).</summary>
    private static bool IsIn([NotNullWhen(true)] string? code, IEnumerable<string> allowed) =>
        code is not null && allowed.Contains(code);

    /// <summary>Trimmed, or null if blank. SCB sends "" or " " for missing text.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // SCB sends UTC, and a plain date arrives as Swedish midnight (2025-02-02T23:00Z is
    // 3 February), so convert to Swedish time before taking the date.
    private static DateOnly? ToSwedishDate(string? utc) =>
        DateTimeOffset.TryParse(
            utc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var moment)
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, Stockholm).DateTime)
            : null;

    // Ten digits, the third one 2-9: the same check as ck_company_orgnr on the company table.
    [GeneratedRegex("^[0-9]{2}[2-9][0-9]{7}$")]
    private static partial Regex OrganisationNumber();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "SCB ingest for SNI {Sni} in {MunicipalityCount} municipalities saved: {Result}")]
    private static partial void LogSaved(
        ILogger logger,
        string sni,
        int municipalityCount,
        ScbIngestResult result);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "SCB ingest for SNI {Sni} in {MunicipalityCount} municipalities completed: {Result}")]
    private static partial void LogCompleted(
        ILogger logger,
        string sni,
        int municipalityCount,
        ScbIngestResult result);

    /// <summary>The companies fetched since the last commit.</summary>
    private sealed class SaveBatch
    {
        public List<Company> Companies { get; } = [];

        public List<Workplace> Workplaces { get; } = [];

        /// <summary>Orgnr that may not be shown, so they are deleted if stored.</summary>
        public List<string> NotEligible { get; } = [];

        /// <summary>How many SCB calls this batch holds, stored or not.</summary>
        public int Count => Companies.Count + NotEligible.Count;
    }
}
