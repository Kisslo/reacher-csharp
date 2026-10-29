using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Reacher.Ingestion.Options;
using Reacher.Ingestion.Scb;
using static Reacher.Ingestion.Tests.Scb.ScbTestData;

namespace Reacher.Ingestion.Tests.Scb;

public sealed class ScbIngestServiceTests(IngestPostgresFixture postgres)
    : IClassFixture<IngestPostgresFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_eligible_companies_and_workplaces_are_stored()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                // The same company in Solna, which wasn't asked for.
                Workplace("90000002", CompanyA, municipality: Solna),
                Workplace("90000003", CompanyA, aeStat: "9"),
                Workplace("90000004", CompanyA, reklamSparrTyp: "2"),
                // An enskild firma: dropped before anything else.
                Workplace("90000005", Personnummer),
                Workplace("90000006", CompanyB),
                Workplace("90000007", CompanyC),
                Workplace("90000008", CompanyD),
            },
            LegalUnits =
            {
                [CompanyA] = LegalUnit(CompanyA),
                // An oskiftat dödsbo (jurform 91), with a 16 number so it reaches the rule.
                [CompanyB] = LegalUnit(CompanyB, jurform: "91"),
                [CompanyC] = LegalUnit(CompanyC, ftgStat: "9"),
                [CompanyD] = LegalUnit(CompanyD, reklamSparrTyp: "2"),
            },
        };

        var result = await Run(scb, [Stockholm]);

        await using var db = postgres.CreateContext();
        Assert.Equal(["5599001236"], await db.Companies.Select(c => c.Orgnr).ToListAsync());
        Assert.Equal(["90000001"], await db.Workplaces.Select(w => w.Cfar).ToListAsync());
        Assert.Equal(1, result.NotLegalPerson);
        Assert.Equal(1, result.OtherMunicipality);
        Assert.Equal(2, result.WorkplaceNotEligible);
        Assert.Equal(3, result.CompanyNotEligible);
    }

    [Theory]
    [InlineData("10", "1", "1")] // enskild firma as a legal form
    [InlineData("91", "1", "1")] // oskiftat dödsbo
    [InlineData("49", "9", "1")] // no longer active
    [InlineData("49", "0", "1")] // never active
    [InlineData("49", "1", "2")] // has opted out of advertising
    [InlineData(null, "1", "1")] // a missing code fails closed
    [InlineData("49", null, "1")]
    [InlineData("49", "1", null)]
    public async Task A_company_is_left_out_unless_every_code_is_in_the_allow_list(
        string? jurform,
        string? ftgStat,
        string? reklamSparrTyp)
    {
        var scb = new FakeScbClient
        {
            Workplaces = { Workplace("90000001", CompanyA) },
            LegalUnits =
            {
                [CompanyA] = LegalUnit(
                    CompanyA, jurform: jurform, ftgStat: ftgStat, reklamSparrTyp: reklamSparrTyp),
            },
        };

        await Run(scb, [Stockholm]);

        await using var db = postgres.CreateContext();
        Assert.Empty(await db.Companies.ToListAsync());
        Assert.Empty(await db.Workplaces.ToListAsync());
    }

    [Fact]
    public async Task A_personnummer_is_never_fetched_stored_or_logged()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", Personnummer),
                Workplace("90000002", CompanyA),
                Workplace("90000003", OtherPersonnummer),
            },
            LegalUnits = { [CompanyA] = LegalUnit(CompanyA) },
        };
        var logs = new CapturingLoggerProvider();
        using var loggers = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(logs));

        var result = await Run(scb, [Stockholm], loggers.CreateLogger<ScbIngestService>());

        Assert.Equal(2, result.NotLegalPerson);
        Assert.Equal([CompanyA], scb.LegalUnitRequests);
        await using var db = postgres.CreateContext();
        Assert.Equal(["5599001236"], await db.Companies.Select(c => c.Orgnr).ToListAsync());
        Assert.Equal(["90000002"], await db.Workplaces.Select(w => w.Cfar).ToListAsync());

        // Something was logged, and the last 10 digits (with or without the 19/20) weren't.
        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(logs.Lines, line =>
            line.Contains(Personnummer[2..], StringComparison.Ordinal)
            || line.Contains(OtherPersonnummer[2..], StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_run_deletes_what_SCB_no_longer_lists()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyA),
                Workplace("90000003", CompanyB),
            },
            LegalUnits =
            {
                [CompanyA] = LegalUnit(CompanyA),
                [CompanyB] = LegalUnit(CompanyB),
            },
        };
        await Run(scb, [Stockholm]);

        // Company A closed one workplace, and company B's only workplace is gone.
        scb.Workplaces.RemoveAll(w => w.CfarNr is "90000002" or "90000003");
        var result = await Run(scb, [Stockholm]);

        await using var db = postgres.CreateContext();
        Assert.Equal(["5599001236"], await db.Companies.Select(c => c.Orgnr).ToListAsync());
        Assert.Equal(["90000001"], await db.Workplaces.Select(w => w.Cfar).ToListAsync());
        Assert.Equal(2, result.WorkplacesDeleted);
        Assert.Equal(1, result.CompaniesDeleted);
    }

    [Fact]
    public async Task Cleanup_leaves_municipalities_that_were_not_requested_alone()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyA, municipality: Solna),
            },
            LegalUnits = { [CompanyA] = LegalUnit(CompanyA) },
        };
        await Run(scb, [Stockholm, Solna]);

        // Stockholm only: the Solna workplace isn't kept by this run, but it isn't deleted.
        var result = await Run(scb, [Stockholm]);

        await using var db = postgres.CreateContext();
        Assert.Equal(
            ["90000001", "90000002"],
            await db.Workplaces.OrderBy(w => w.Cfar).Select(w => w.Cfar).ToListAsync());
        Assert.Equal(0, result.WorkplacesDeleted);
    }

    [Fact]
    public async Task A_company_that_is_no_longer_eligible_is_deleted_with_all_its_workplaces()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyA, municipality: Solna),
            },
            LegalUnits = { [CompanyA] = LegalUnit(CompanyA) },
        };
        await Run(scb, [Stockholm, Solna]);

        // The company has opted out of advertising since the first run.
        scb.LegalUnits[CompanyA] = LegalUnit(CompanyA, reklamSparrTyp: "2");
        await Run(scb, [Stockholm]);

        // Also the Solna workplace, although Solna wasn't part of this search.
        await using var db = postgres.CreateContext();
        Assert.Empty(await db.Companies.ToListAsync());
        Assert.Empty(await db.Workplaces.ToListAsync());
    }

    [Fact]
    public async Task Each_company_is_fetched_once_per_run()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyA),
                Workplace("90000003", CompanyA),
            },
            LegalUnits = { [CompanyA] = LegalUnit(CompanyA) },
        };

        await Run(scb, [Stockholm]);

        Assert.Equal([CompanyA], scb.LegalUnitRequests);
    }

    [Fact]
    public async Task Fields_are_mapped_trimmed_and_dated_in_Swedish_time()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA, ben: "  Påhittade Saxen Söder "),
                Workplace("90000002", CompanyB, ben: " "),
            },
            LegalUnits =
            {
                // A plain date is sent as Swedish midnight in UTC: this is 3 February.
                [CompanyA] = LegalUnit(
                    CompanyA,
                    foretagsnamn: " Påhittade Saxen AB ",
                    regDat: "2025-02-02T23:00:00.000Z"),
                // No foretagsnamn, so the name falls back to namn.
                [CompanyB] = LegalUnit(CompanyB, foretagsnamn: "", namn: "Låtsasbolaget AB"),
            },
        };

        await Run(scb, [Stockholm, Solna]);

        await using var db = postgres.CreateContext();
        var company = await db.Companies.SingleAsync(c => c.Orgnr == "5599001236");
        Assert.Equal("Påhittade Saxen AB", company.Name);
        Assert.Equal(new DateOnly(2025, 2, 3), company.RegisteredAt);
        Assert.Equal(Sni, company.Sni);
        Assert.Equal("49", company.LegalForm);
        Assert.Equal("1", company.CompanyStatus);
        Assert.Equal("3", company.EmployeeClass);
        Assert.Equal("3", company.RevenueClass);
        Assert.Equal(2024, company.RevenueClassYear);
        Assert.Equal("1", company.AdBlockType);
        Assert.Equal(Now, company.UpdatedAt);
        var other = await db.Companies.SingleAsync(c => c.Orgnr == "5599002234");
        Assert.Equal("Låtsasbolaget AB", other.Name);

        var workplace = await db.Workplaces.SingleAsync(w => w.Cfar == "90000001");
        Assert.Equal("5599001236", workplace.Orgnr);
        Assert.Equal("Påhittade Saxen Söder", workplace.Name);
        Assert.Equal(Stockholm, workplace.Municipality);
        Assert.Equal("Påhittade gatan 1", workplace.Street);
        Assert.Equal("11850", workplace.PostalCode);
        Assert.Equal("STOCKHOLM", workplace.City);
        Assert.Equal("1", workplace.WorkplaceStatus);
        Assert.Equal("1", workplace.AdBlockType);
        Assert.Null((await db.Workplaces.SingleAsync(w => w.Cfar == "90000002")).Name);

        // Solna had no match but is fresh too, so the next search there starts no ingest.
        var coverage = await db.Coverage.OrderBy(c => c.Municipality).ToListAsync();
        Assert.Equal([Stockholm, Solna], coverage.Select(c => c.Municipality));
        Assert.All(coverage, c => Assert.Equal(Now, c.FetchedAt));
    }

    [Fact]
    public async Task Progress_counts_pages_then_companies()
    {
        // The page size is 2 in these tests, so three rows are two pages.
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyA),
                Workplace("90000003", CompanyA),
            },
            LegalUnits = { [CompanyA] = LegalUnit(CompanyA) },
        };
        var progress = new RecordingProgress();

        await Run(scb, [Stockholm], progress: progress);

        Assert.Equal(
            [
                new IngestProgress(IngestPhases.Workplaces, 0, null),
                new IngestProgress(IngestPhases.Workplaces, 1, null),
                new IngestProgress(IngestPhases.Workplaces, 2, 2),
                new IngestProgress(IngestPhases.Companies, 0, 1),
                new IngestProgress(IngestPhases.Companies, 1, 1),
            ],
            progress.Reports);
    }

    private async Task<ScbIngestResult> Run(
        FakeScbClient scb,
        string[] municipalities,
        ILogger<ScbIngestService>? logger = null,
        IProgress<IngestProgress>? progress = null)
    {
        await using var db = postgres.CreateContext();
        // Fully qualified: inside Reacher.Ingestion, "Options" is our own options namespace.
        var service = new ScbIngestService(
            scb,
            db,
            Microsoft.Extensions.Options.Options.Create(new ScbOptions { PageSize = 2 }),
            Microsoft.Extensions.Options.Options.Create(
                new IngestOptions { AllowedLegalForms = ["49"] }),
            new FixedTimeProvider(Now),
            logger ?? NullLogger<ScbIngestService>.Instance);
        return await service.RunAsync(
            Sni, municipalities, progress ?? new RecordingProgress(), CancellationToken.None);
    }
}
