using Microsoft.Extensions.Logging.Abstractions;
using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Jobs;
using Reacher.Ingestion.Options;
using Reacher.Ingestion.Scb;
using Reacher.Ingestion.Tests.Bolagsverket;
using Reacher.Ingestion.Tests.Scb;
using static Reacher.Ingestion.Tests.Scb.ScbTestData;

namespace Reacher.Ingestion.Tests.Jobs;

public sealed class IngestPipelineTests(IngestPostgresFixture postgres)
    : IClassFixture<IngestPostgresFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Financials_are_only_fetched_for_companies_touched_by_the_job()
    {
        var scb = new FakeScbClient
        {
            Workplaces =
            {
                Workplace("90000001", CompanyA),
                Workplace("90000002", CompanyB, municipality: Solna),
            },
            LegalUnits =
            {
                [CompanyA] = LegalUnit(CompanyA),
                [CompanyB] = LegalUnit(CompanyB),
            },
        };
        var bolagsverket = new FakeBolagsverketClient();
        bolagsverket.Add(CompanyA[2..], "DOC-A", new DateOnly(2026, 4, 30), AnnualReports.Fixture());
        bolagsverket.Add(CompanyB[2..], "DOC-B", new DateOnly(2026, 4, 30), AnnualReports.Fixture());

        // An earlier search stored company B in Solna.
        await Run(scb, bolagsverket, [Solna]);
        bolagsverket.Downloads.Clear();

        await Run(scb, bolagsverket, [Stockholm]);

        Assert.Equal(["DOC-A"], bolagsverket.Downloads);
    }

    [Fact]
    public async Task Progress_goes_through_every_phase()
    {
        var scb = new FakeScbClient
        {
            Workplaces = { Workplace("90000001", CompanyA) },
            LegalUnits = { [CompanyA] = LegalUnit(CompanyA) },
        };
        var progress = new RecordingProgress();

        await Run(scb, new FakeBolagsverketClient(), [Stockholm], progress);

        Assert.Equal(
            [IngestPhases.Workplaces, IngestPhases.Companies, IngestPhases.Financials],
            progress.Reports.Select(report => report.Phase).Distinct());
        Assert.Equal(new IngestProgress(IngestPhases.Financials, 1, 1), progress.Reports[^1]);
    }

    private async Task Run(
        FakeScbClient scb,
        FakeBolagsverketClient bolagsverket,
        string[] municipalities,
        IProgress<IngestProgress>? progress = null)
    {
        await using var db = postgres.CreateContext();
        var time = new FixedTimeProvider(Now);
        // Fully qualified: inside Reacher.Ingestion, "Options" is our own options namespace.
        var pipeline = new IngestPipeline(
            new ScbIngestService(
                scb,
                db,
                Microsoft.Extensions.Options.Options.Create(new ScbOptions { PageSize = 2 }),
                Microsoft.Extensions.Options.Options.Create(
                    new IngestOptions { AllowedLegalForms = ["49"] }),
                time,
                NullLogger<ScbIngestService>.Instance),
            new FinancialsIngestService(
                bolagsverket,
                new IxbrlParser(NullLogger<IxbrlParser>.Instance),
                db,
                Microsoft.Extensions.Options.Options.Create(new BolagsverketOptions
                {
                    Years = 3,
                    TagMap =
                    [
                        new BolagsverketTag { Tag = "se-gen-base:Nettoomsattning", Fact = "revenue" },
                        new BolagsverketTag { Tag = "se-gen-base:AretsResultat", Fact = "net_result" },
                    ],
                }),
                time,
                NullLogger<FinancialsIngestService>.Instance),
            db);
        await pipeline.RunAsync(
            Sni, municipalities, progress ?? new RecordingProgress(), CancellationToken.None);
    }
}
