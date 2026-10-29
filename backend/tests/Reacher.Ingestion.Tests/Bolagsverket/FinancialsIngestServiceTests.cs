using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Reacher.Data.Entities;
using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Options;
using Reacher.Ingestion.Tests.Scb;
using static Reacher.Ingestion.Tests.Bolagsverket.AnnualReports;

namespace Reacher.Ingestion.Tests.Bolagsverket;

public sealed class FinancialsIngestServiceTests(IngestPostgresFixture postgres)
    : IClassFixture<IngestPostgresFixture>, IAsyncLifetime
{
    // Made-up orgnr, as stored in company (10 digits, no 16 prefix).
    private const string CompanyA = "5599001236";
    private const string CompanyB = "5599002234";
    private const string CompanyC = "5599003232";

    private static readonly DateOnly Year2023 = new(2023, 4, 30);
    private static readonly DateOnly Year2024 = new(2024, 4, 30);
    private static readonly DateOnly Year2025 = new(2025, 4, 30);
    private static readonly DateOnly Year2026 = new(2026, 4, 30);

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();

        // financial_fact has a foreign key to company, so the companies come first.
        await using var db = postgres.CreateContext();
        db.Companies.AddRange(NewCompany(CompanyA), NewCompany(CompanyB), NewCompany(CompanyC));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_report_gives_both_figures_for_its_own_year_and_the_comparison_year()
    {
        var bolagsverket = new FakeBolagsverketClient();
        bolagsverket.Add(CompanyA, "DOC-2026", Year2026, Fixture());

        var result = await Run(bolagsverket, [CompanyA]);

        Assert.Equal(
            [
                (Year2026, "net_result", 126_455m, "DOC-2026"),
                (Year2026, "revenue", 4_702_330m, "DOC-2026"),
                (Year2025, "net_result", -48_210m, "DOC-2026"),
                (Year2025, "revenue", 4_388_615m, "DOC-2026"),
            ],
            await FactsAsync(CompanyA));
        await using var db = postgres.CreateContext();
        Assert.All(
            await db.FinancialFacts.ToListAsync(),
            f => Assert.Equal(ScbTestData.Now, f.FetchedAt));
        Assert.Equal(4, result.FactsStored);
    }

    [Fact]
    public async Task With_two_of_three_years_stored_only_one_report_is_downloaded()
    {
        await StoreAsync(CompanyA, Year2026, "DOC-2026");
        await StoreAsync(CompanyA, Year2025, "DOC-2026");
        var bolagsverket = new FakeBolagsverketClient();
        bolagsverket.Add(
            CompanyA, "DOC-2026", Year2026, Report((Year2026, 300, 30), (Year2025, 200, 20)));
        bolagsverket.Add(
            CompanyA, "DOC-2025", Year2025, Report((Year2025, 200, 20), (Year2024, 100, 10)));
        bolagsverket.Add(
            CompanyA, "DOC-2024", Year2024, Report((Year2024, 100, 10), (Year2023, 50, 5)));

        var result = await Run(bolagsverket, [CompanyA]);

        Assert.Equal(["DOC-2024"], bolagsverket.Downloads);
        Assert.Equal([Year2026, Year2025, Year2024], await YearsAsync(CompanyA));
        Assert.Equal(2, result.ReportsSkipped);
        Assert.Equal("3: 1", result.YearsStored);
    }

    [Fact]
    public async Task A_first_run_skips_a_report_whose_year_came_with_a_newer_report()
    {
        var bolagsverket = new FakeBolagsverketClient();
        // DOC-2026 restates 2025 (210 instead of 200), so its figure is the one to keep.
        bolagsverket.Add(
            CompanyA, "DOC-2026", Year2026, Report((Year2026, 300, 30), (Year2025, 210, 20)));
        bolagsverket.Add(
            CompanyA, "DOC-2025", Year2025, Report((Year2025, 200, 20), (Year2024, 100, 10)));
        bolagsverket.Add(
            CompanyA, "DOC-2024", Year2024, Report((Year2024, 100, 10), (Year2023, 50, 5)));
        bolagsverket.Add(CompanyA, "DOC-2023", Year2023, Report((Year2023, 50, 5)));

        var result = await Run(bolagsverket, [CompanyA]);

        // 2023 comes with DOC-2024, but only the 3 latest years are kept.
        Assert.Equal(["DOC-2026", "DOC-2024"], bolagsverket.Downloads);
        Assert.Equal([Year2026, Year2025, Year2024], await YearsAsync(CompanyA));
        Assert.Contains((Year2025, "revenue", 210m, "DOC-2026"), await FactsAsync(CompanyA));
        Assert.Equal(1, result.ReportsSkipped);
    }

    [Fact]
    public async Task A_newer_report_replaces_a_restated_comparison_figure()
    {
        // Stored by a run last year, before DOC-2026 was filed.
        await StoreAsync(CompanyA, Year2025, "DOC-2025", revenue: 200);
        await StoreAsync(CompanyA, Year2024, "DOC-2024");
        var bolagsverket = new FakeBolagsverketClient();
        bolagsverket.Add(
            CompanyA, "DOC-2026", Year2026, Report((Year2026, 300, 30), (Year2025, 210, 21)));
        bolagsverket.Add(
            CompanyA, "DOC-2025", Year2025, Report((Year2025, 200, 20), (Year2024, 100, 10)));
        bolagsverket.Add(CompanyA, "DOC-2024", Year2024, Report((Year2024, 100, 10)));

        await Run(bolagsverket, [CompanyA]);

        Assert.Equal(["DOC-2026"], bolagsverket.Downloads);
        var facts = await FactsAsync(CompanyA);
        Assert.Contains((Year2025, "revenue", 210m, "DOC-2026"), facts);
        Assert.Contains((Year2025, "net_result", 21m, "DOC-2026"), facts);
    }

    [Fact]
    public async Task A_corrected_report_is_used_instead_of_the_first_filing()
    {
        var bolagsverket = new FakeBolagsverketClient();
        bolagsverket.Add(
            CompanyA, "DOC-2026", Year2026, Report((Year2026, 300, 30)), "2026-09-17T10:00:00");
        bolagsverket.Add(
            CompanyA, "DOC-2026-FIX", Year2026, Report((Year2026, 310, 31)), "2026-10-02T08:00:00");

        await Run(bolagsverket, [CompanyA]);

        Assert.Equal(["DOC-2026-FIX"], bolagsverket.Downloads);
        Assert.Contains((Year2026, "revenue", 310m, "DOC-2026-FIX"), await FactsAsync(CompanyA));
    }

    [Fact]
    public async Task A_parse_error_for_one_company_does_not_stop_the_others()
    {
        var bolagsverket = new FakeBolagsverketClient();
        bolagsverket.Add(CompanyA, "DOC-A", Year2026, Report((Year2026, 100, 10)));
        bolagsverket.Add(CompanyB, "DOC-B-BROKEN", Year2026, "<html><body>never closed");
        bolagsverket.Add(CompanyC, "DOC-C", Year2026, Report((Year2026, 300, 30)));
        var logs = new CapturingLoggerProvider();
        using var loggers = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(logs));

        var result = await Run(bolagsverket, [CompanyA, CompanyB, CompanyC], loggers);

        Assert.Equal(1, result.CompaniesFailed);
        Assert.Equal([Year2026], await YearsAsync(CompanyA));
        Assert.Empty(await YearsAsync(CompanyB));
        Assert.Equal([Year2026], await YearsAsync(CompanyC));

        // The failure is logged with the document id, and no orgnr is logged at all.
        Assert.Contains(
            logs.Lines,
            line => line.Contains("DOC-B-BROKEN", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line =>
            line.Contains(CompanyA, StringComparison.Ordinal)
            || line.Contains(CompanyB, StringComparison.Ordinal)
            || line.Contains(CompanyC, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_company_without_annual_reports_gets_no_rows()
    {
        var result = await Run(new FakeBolagsverketClient(), [CompanyA]);

        Assert.Empty(await YearsAsync(CompanyA));
        Assert.Equal(1, result.CompaniesWithoutReports);
        Assert.Equal("0: 1", result.YearsStored);
    }

    [Fact]
    public async Task A_year_without_revenue_has_no_revenue_row_never_a_zero()
    {
        // K2 reports often leave empty lines out, so this one has a net result only.
        var bolagsverket = new FakeBolagsverketClient();
        bolagsverket.Add(CompanyA, "DOC-2026", Year2026, Report((Year2026, null, -5_000)));

        await Run(bolagsverket, [CompanyA]);

        Assert.Equal([(Year2026, "net_result", -5_000m, "DOC-2026")], await FactsAsync(CompanyA));
    }

    [Fact]
    public async Task Wrong_credentials_stop_the_run()
    {
        var bolagsverket = new FakeBolagsverketClient
        {
            Failure = new BolagsverketAuthException("POST /dokumentlista: HTTP 401."),
        };

        await Assert.ThrowsAsync<BolagsverketAuthException>(
            () => Run(bolagsverket, [CompanyA, CompanyB]));
    }

    [Fact]
    public async Task Progress_counts_companies()
    {
        var bolagsverket = new FakeBolagsverketClient();
        bolagsverket.Add(CompanyA, "DOC-2026", Year2026, Report((Year2026, 100, 10)));
        var progress = new RecordingProgress();

        await Run(bolagsverket, [CompanyA, CompanyB], progress: progress);

        Assert.Equal(
            [
                new IngestProgress(IngestPhases.Financials, 0, 2),
                new IngestProgress(IngestPhases.Financials, 1, 2),
                new IngestProgress(IngestPhases.Financials, 2, 2),
            ],
            progress.Reports);
    }

    private async Task<FinancialsIngestResult> Run(
        FakeBolagsverketClient bolagsverket,
        string[] orgnrs,
        ILoggerFactory? loggers = null,
        IProgress<IngestProgress>? progress = null)
    {
        loggers ??= NullLoggerFactory.Instance;
        await using var db = postgres.CreateContext();
        // Fully qualified: inside Reacher.Ingestion, "Options" is our own options namespace.
        var service = new FinancialsIngestService(
            bolagsverket,
            new IxbrlParser(loggers.CreateLogger<IxbrlParser>()),
            db,
            Microsoft.Extensions.Options.Options.Create(new BolagsverketOptions
            {
                Years = 3,
                // The same pairs as Bolagsverket:TagMap in appsettings.json.
                TagMap =
                [
                    new BolagsverketTag { Tag = "se-gen-base:Nettoomsattning", Fact = "revenue" },
                    new BolagsverketTag { Tag = "se-gen-base:AretsResultat", Fact = "net_result" },
                ],
            }),
            new FixedTimeProvider(ScbTestData.Now),
            loggers.CreateLogger<FinancialsIngestService>());
        return await service.RunAsync(
            orgnrs, progress ?? new RecordingProgress(), CancellationToken.None);
    }

    /// <summary>A revenue row an earlier run stored from <paramref name="documentId"/>.</summary>
    private async Task StoreAsync(
        string orgnr,
        DateOnly periodEnd,
        string documentId,
        decimal revenue = 1)
    {
        await using var db = postgres.CreateContext();
        db.FinancialFacts.Add(new FinancialFact
        {
            Orgnr = orgnr,
            PeriodEnd = periodEnd,
            Key = "revenue",
            Value = revenue,
            DocumentId = documentId,
            FetchedAt = ScbTestData.Now.AddYears(-1),
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<(DateOnly PeriodEnd, string Key, decimal Value, string DocumentId)>>
        FactsAsync(string orgnr)
    {
        await using var db = postgres.CreateContext();
        var facts = await db.FinancialFacts
            .Where(f => f.Orgnr == orgnr)
            .OrderByDescending(f => f.PeriodEnd)
            .ThenBy(f => f.Key)
            .ToListAsync();
        return [.. facts.Select(f => (f.PeriodEnd, f.Key, f.Value, f.DocumentId))];
    }

    /// <summary>The fiscal years stored for the company, newest first.</summary>
    private async Task<List<DateOnly>> YearsAsync(string orgnr)
    {
        await using var db = postgres.CreateContext();
        return await db.FinancialFacts
            .Where(f => f.Orgnr == orgnr)
            .Select(f => f.PeriodEnd)
            .Distinct()
            .OrderByDescending(periodEnd => periodEnd)
            .ToListAsync();
    }

    private static Company NewCompany(string orgnr) => new()
    {
        Orgnr = orgnr,
        Name = "Påhittade Saxen AB",
        Sni = ScbTestData.Sni,
        LegalForm = "49",
        CompanyStatus = "1",
        EmployeeClass = "3",
        RevenueClass = "3",
        RevenueClassYear = 2024,
        AdBlockType = "1",
        UpdatedAt = ScbTestData.Now,
    };
}
