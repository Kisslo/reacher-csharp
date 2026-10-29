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
/// Only eligible aktiebolag are stored (D2, D5). A personnummer is dropped as the very first
/// step: it is never mapped, fetched, logged or stored. Logs hold counts only.
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

    public async Task<ScbIngestResult> RunAsync(
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
        var now = time.GetUtcNow();
        var result = new ScbIngestResult();
        var wanted = municipalities.ToHashSet(StringComparer.Ordinal);

        var byCompany = await ReadWorkplacesAsync(
            sni, wanted, now, progress, result, cancellationToken);
        var (companies, workplaces, notEligible) = await FetchCompaniesAsync(
            sni, byCompany, now, progress, result, cancellationToken);
        await SaveAsync(
            sni, wanted, companies, workplaces, notEligible, now, result, cancellationToken);

        result.Duration = time.GetElapsedTime(started);
        LogFinished(logger, sni, wanted.Count, result);
        return result;
    }

    /// <summary>
    /// Steps 1-3: walks every page and keeps the workplaces that may be stored, by orgnr.
    /// </summary>
    private async Task<Dictionary<string, List<Workplace>>> ReadWorkplacesAsync(
        string sni,
        HashSet<string> wanted,
        DateTimeOffset now,
        IProgress<IngestProgress> progress,
        ScbIngestResult result,
        CancellationToken cancellationToken)
    {
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
                || !wanted.Contains(municipality))
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
                UpdatedAt = now,
            });
        }

        var pages = (result.WorkplacesRead + pageSize - 1) / pageSize;
        progress.Report(new IngestProgress(IngestPhases.Workplaces, pages, pages));
        return byCompany;
    }

    /// <summary>
    /// Step 4: one call per company, however many workplaces it has (the cache per run).
    /// </summary>
    private async Task<(List<Company> Companies, List<Workplace> Workplaces, List<string> NotEligible)>
        FetchCompaniesAsync(
            string sni,
            Dictionary<string, List<Workplace>> byCompany,
            DateTimeOffset now,
            IProgress<IngestProgress> progress,
            ScbIngestResult result,
            CancellationToken cancellationToken)
    {
        var companies = new List<Company>();
        var workplaces = new List<Workplace>();
        var notEligible = new List<string>();
        var done = 0;
        progress.Report(new IngestProgress(IngestPhases.Companies, 0, byCompany.Count));

        foreach (var (orgnr, companyWorkplaces) in byCompany)
        {
            var unit = await scb.GetLegalUnit(LegalPersonPrefix + orgnr, cancellationToken);
            result.CompaniesFetched++;
            if (unit is null)
            {
                result.CompanyNotFound++;
            }

            if (unit is not null && ToEligibleCompany(orgnr, unit, sni, now, result) is { } company)
            {
                companies.Add(company);
                workplaces.AddRange(companyWorkplaces);
            }
            else
            {
                // Fail closed: also removes the company if an earlier run stored it.
                notEligible.Add(orgnr);
            }

            done++;
            progress.Report(new IngestProgress(IngestPhases.Companies, done, byCompany.Count));
        }

        return (companies, workplaces, notEligible);
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
    /// Steps 5-7 in one transaction, so a failed or cancelled run leaves the tables as they
    /// were.
    /// </summary>
    private async Task SaveAsync(
        string sni,
        HashSet<string> wanted,
        List<Company> companies,
        List<Workplace> workplaces,
        List<string> notEligible,
        DateTimeOffset now,
        ScbIngestResult result,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // A company SCB says may no longer be shown goes completely, with its workplaces in
        // other municipalities and its financial facts (ON DELETE CASCADE).
        result.CompaniesDeleted += await db.Companies
            .Where(c => notEligible.Contains(c.Orgnr))
            .ExecuteDeleteAsync(cancellationToken);

        await UpsertCompaniesAsync(companies, cancellationToken);
        await UpsertWorkplacesAsync(workplaces, cancellationToken);
        await UpsertCoverageAsync(sni, wanted, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        result.CompaniesStored = companies.Count;
        result.WorkplacesStored = workplaces.Count;

        // Step 6: what SCB no longer lists in the requested municipalities is gone. Other
        // municipalities belong to other searches and are left alone.
        var municipalities = wanted.ToList();
        var seen = workplaces.Select(w => w.Cfar).ToList();
        result.WorkplacesDeleted = await db.Workplaces
            .Where(w => w.Sni == sni
                && municipalities.Contains(w.Municipality)
                && !seen.Contains(w.Cfar))
            .ExecuteDeleteAsync(cancellationToken);

        // A company is only shown through its workplaces (D15), so one without any goes too.
        result.CompaniesDeleted += await db.Companies
            .Where(c => !db.Workplaces.Any(w => w.Orgnr == c.Orgnr))
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
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
    /// so the next search there doesn't start a new ingest (D6).
    /// </summary>
    private async Task UpsertCoverageAsync(
        string sni,
        HashSet<string> municipalities,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var municipality in municipalities)
        {
            var coverage = await db.Coverage.FindAsync([sni, municipality], cancellationToken);
            if (coverage is null)
            {
                db.Coverage.Add(new Coverage
                {
                    Sni = sni,
                    Municipality = municipality,
                    FetchedAt = now,
                });
            }
            else
            {
                coverage.FetchedAt = now;
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
        Message = "SCB ingest for SNI {Sni} in {MunicipalityCount} municipalities: {Result}")]
    private static partial void LogFinished(
        ILogger logger,
        string sni,
        int municipalityCount,
        ScbIngestResult result);
}
