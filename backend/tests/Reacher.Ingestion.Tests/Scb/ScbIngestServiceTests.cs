using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Reacher.Data.Entities;
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
    public async Task A_cancelled_run_stops_calling_SCB_and_keeps_what_was_saved()
    {
        var scb = new FakeScbClient
        {
            Workplaces = { Workplace("90000001", CompanyA), Workplace("90000002", CompanyA) },
            LegalUnits = { [CompanyA] = LegalUnit(CompanyA) },
        };
        await Run(scb, [Stockholm]);

        // SCB no longer lists one workplace, and company B in Solna is new.
        scb.Workplaces.RemoveAll(w => w.CfarNr == "90000002");
        scb.Workplaces.Add(Workplace("90000003", CompanyB, municipality: Solna));
        scb.LegalUnits[CompanyB] = LegalUnit(CompanyB);
        scb.LegalUnitRequests.Clear();
        using var cancel = new CancellationTokenSource();
        scb.OnLegalUnit = _ => cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Run(scb, [Stockholm, Solna], cancellationToken: cancel.Token));

        // Cancelled after the first company: no further SCB calls.
        Assert.Single(scb.LegalUnitRequests);
        await using var db = postgres.CreateContext();
        // The workplace SCB stopped listing is not deleted, and Solna is not marked fresh.
        Assert.Equal(
            ["90000001", "90000002"],
            await db.Workplaces.OrderBy(w => w.Cfar).Select(w => w.Cfar).ToListAsync());
        Assert.Equal([Stockholm], await db.Coverage.Select(c => c.Municipality).ToListAsync());
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

    [Fact]
    public async Task Each_batch_is_committed_before_the_run_ends()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyB),
                Workplace("90000003", CompanyC),
            },
            LegalUnits =
            {
                [CompanyA] = LegalUnit(CompanyA),
                [CompanyB] = LegalUnit(CompanyB),
                [CompanyC] = LegalUnit(CompanyC),
            },
        };
        List<string>? companiesMidRun = null;
        int? coverageMidRun = null;
        scb.OnLegalUnit = peOrgNr =>
        {
            if (peOrgNr != CompanyC)
            {
                return;
            }

            // What GET /api/companies reads while the job is still running. The hook is
            // synchronous, so the queries are too.
            using var other = postgres.CreateContext();
            companiesMidRun = [.. other.Companies.OrderBy(c => c.Orgnr).Select(c => c.Orgnr)];
            coverageMidRun = other.Coverage.Count();
        };

        await Run(scb, [Stockholm], saveBatchSize: 2);

        // The first batch (A and B) was visible, and the data was not marked fresh yet.
        Assert.Equal(["5599001236", "5599002234"], companiesMidRun);
        Assert.Equal(0, coverageMidRun);
        await using var db = postgres.CreateContext();
        Assert.Equal(3, await db.Companies.CountAsync());
        Assert.Equal(1, await db.Coverage.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_or_cancelled_run_keeps_its_batches_but_skips_coverage_and_cleanup(
        bool cancel)
    {
        // A first search stored A, B and C, and someone has since called B and C.
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyB),
                Workplace("90000003", CompanyC),
            },
            LegalUnits =
            {
                [CompanyA] = LegalUnit(CompanyA),
                [CompanyB] = LegalUnit(CompanyB),
                [CompanyC] = LegalUnit(CompanyC),
            },
        };
        await Run(scb, [Stockholm]);
        await AddOutreachAsync("5599002234");
        await AddOutreachAsync("5599003232");

        // A day later A has a new name, D is new and SCB no longer lists B. The run then
        // fails, or the user cancels it, at C, the last company.
        scb.Workplaces.Clear();
        scb.Workplaces.AddRange(
        [
            Workplace("90000001", CompanyA),
            Workplace("90000004", CompanyD),
            Workplace("90000003", CompanyC),
        ]);
        scb.LegalUnits[CompanyA] = LegalUnit(CompanyA, foretagsnamn: "Påhittade Saxen Nya AB");
        scb.LegalUnits[CompanyD] = LegalUnit(CompanyD);
        using var cancellation = new CancellationTokenSource();
        scb.OnLegalUnit = peOrgNr =>
        {
            if (peOrgNr != CompanyC)
            {
                return;
            }

            if (cancel)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }

            throw new ScbException("GET /juridiskaenheter/{peOrgNr}/full: HTTP 500.");
        };
        var later = Now.AddDays(1);

        Task Rerun() => Run(
            scb, [Stockholm], saveBatchSize: 1, now: later, cancellationToken: cancellation.Token);
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(Rerun);
        }
        else
        {
            await Assert.ThrowsAsync<ScbException>(Rerun);
        }

        await using (var db = postgres.CreateContext())
        {
            // The committed batches stay: A's new name and the new company D.
            var a = await db.Companies.SingleAsync(c => c.Orgnr == "5599001236");
            Assert.Equal("Påhittade Saxen Nya AB", a.Name);

            // No cleanup: B, which SCB no longer lists, is still there, and so is all outreach.
            Assert.Equal(
                ["5599001236", "5599002234", "5599003232", "5599004230"],
                await db.Companies.OrderBy(c => c.Orgnr).Select(c => c.Orgnr).ToListAsync());
            Assert.Equal(
                ["5599002234", "5599003232"],
                await db.CompanyOutreach.OrderBy(o => o.Orgnr).Select(o => o.Orgnr).ToListAsync());

            // Not fresh: the coverage is still from the first search.
            Assert.Equal(Now, (await db.Coverage.SingleAsync()).FetchedAt);
        }

        // The next run succeeds: B goes, with its outreach (D18), and the data is fresh.
        scb.OnLegalUnit = null;
        await Run(scb, [Stockholm], now: later);

        await using (var db = postgres.CreateContext())
        {
            Assert.Equal(
                ["5599001236", "5599003232", "5599004230"],
                await db.Companies.OrderBy(c => c.Orgnr).Select(c => c.Orgnr).ToListAsync());
            Assert.Equal(
                ["5599003232"],
                await db.CompanyOutreach.Select(o => o.Orgnr).ToListAsync());
            Assert.Equal(later, (await db.Coverage.SingleAsync()).FetchedAt);
        }
    }

    [Fact]
    public async Task A_workplace_that_turned_ineligible_is_deleted_even_if_the_run_is_cancelled()
    {
        // A first search stored A in Stockholm and Solna, and B in Stockholm.
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyA, municipality: Solna),
                Workplace("90000003", CompanyB),
            },
            LegalUnits =
            {
                [CompanyA] = LegalUnit(CompanyA),
                [CompanyB] = LegalUnit(CompanyB),
            },
        };
        await Run(scb, [Stockholm, Solna]);

        // A's Stockholm workplace has since opted out of advertising. SCB still lists it, so
        // only the eligibility rule can remove it. The user cancels at the first company.
        scb.Workplaces[0] = Workplace("90000001", CompanyA, reklamSparrTyp: "2");
        scb.LegalUnitRequests.Clear();
        using var cancel = new CancellationTokenSource();
        scb.OnLegalUnit = _ => cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Run(scb, [Stockholm], cancellationToken: cancel.Token));

        // A had no eligible workplace in Stockholm, so only B was fetched before the cancel.
        Assert.Equal([CompanyB], scb.LegalUnitRequests);
        await using var db = postgres.CreateContext();
        Assert.False(await db.Workplaces.AnyAsync(w => w.Cfar == "90000001"));

        // The same rule as CompanySearch.sql: a company is in the search only through a
        // workplace with the SNI code in a requested municipality.
        Assert.False(await db.Workplaces.AnyAsync(w =>
            w.Orgnr == "5599001236" && w.Sni == Sni && w.Municipality == Stockholm));

        // Solna wasn't part of this search, and the company itself is still eligible.
        Assert.True(await db.Workplaces.AnyAsync(w => w.Cfar == "90000002"));
        Assert.True(await db.Companies.AnyAsync(c => c.Orgnr == "5599001236"));
    }

    [Fact]
    public async Task Outreach_stays_when_the_company_still_has_an_eligible_workplace()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyA),
            },
            LegalUnits = { [CompanyA] = LegalUnit(CompanyA) },
        };
        await Run(scb, [Stockholm]);
        await AddOutreachAsync("5599001236");

        // One of A's two workplaces is no longer active.
        scb.Workplaces[1] = Workplace("90000002", CompanyA, aeStat: "9");
        var result = await Run(scb, [Stockholm]);

        await using var db = postgres.CreateContext();
        Assert.Equal(["90000001"], await db.Workplaces.Select(w => w.Cfar).ToListAsync());
        Assert.Equal(["5599001236"], await db.Companies.Select(c => c.Orgnr).ToListAsync());
        Assert.Equal(
            ["5599001236"],
            await db.CompanyOutreach.Select(o => o.Orgnr).ToListAsync());

        // Deleted during the run, so the end-of-run cleanup had nothing left to delete.
        Assert.Equal(1, result.IneligibleWorkplacesDeleted);
        Assert.Equal(0, result.WorkplacesDeleted);
        Assert.Equal(0, result.CompaniesDeleted);
    }

    /// <summary>
    /// The SCB part of a whole job: <see cref="ScbIngestService.RunAsync"/>, then
    /// <see cref="ScbIngestService.CompleteAsync"/> as the pipeline calls it on success. If
    /// <c>RunAsync</c> throws, the completion never runs, as in a failed job.
    /// </summary>
    private async Task<ScbIngestResult> Run(
        FakeScbClient scb,
        string[] municipalities,
        ILogger<ScbIngestService>? logger = null,
        IProgress<IngestProgress>? progress = null,
        int saveBatchSize = 10,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = postgres.CreateContext();
        // Fully qualified: inside Reacher.Ingestion, "Options" is our own options namespace.
        var service = new ScbIngestService(
            scb,
            db,
            Microsoft.Extensions.Options.Options.Create(new ScbOptions { PageSize = 2 }),
            Microsoft.Extensions.Options.Options.Create(
                new IngestOptions { AllowedLegalForms = ["49"], SaveBatchSize = saveBatchSize }),
            new FixedTimeProvider(now ?? Now),
            logger ?? NullLogger<ScbIngestService>.Instance);
        var run = await service.RunAsync(
            Sni, municipalities, progress ?? new RecordingProgress(), cancellationToken);
        await service.CompleteAsync(run, cancellationToken);
        return run.Result;
    }

    private async Task AddOutreachAsync(string orgnr)
    {
        await using var db = postgres.CreateContext();
        db.CompanyOutreach.Add(new CompanyOutreach
        {
            Orgnr = orgnr,
            CalledAt = Now,
            Willing = true,
            Note = "Ring igen i januari.",
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();
    }
}
