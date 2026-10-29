using Microsoft.EntityFrameworkCore;
using Reacher.Data.Entities;
using Reacher.Ingestion.Jobs;
using Reacher.Ingestion.Options;
using Reacher.Ingestion.Tests.Scb;
using static Reacher.Ingestion.Tests.Scb.ScbTestData;

namespace Reacher.Ingestion.Tests.Jobs;

public sealed class IngestCoordinatorTests(IngestPostgresFixture postgres)
    : IClassFixture<IngestPostgresFixture>, IAsyncLifetime
{
    private readonly EnqueueRecorder _queue = new();

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Fresh_coverage_gives_no_job()
    {
        await AddCoverageAsync(Stockholm, daysOld: 29);
        await AddCoverageAsync(Solna, daysOld: 0);

        var result = await Request([Stockholm, Solna]);

        Assert.Equal(IngestRequestResult.FreshData, result);
        Assert.Empty(_queue.Enqueued);
        await using var db = postgres.CreateContext();
        Assert.Empty(await db.IngestJobs.ToListAsync());
    }

    [Theory]
    [InlineData(31)] // older than Ingest:FreshnessDays
    [InlineData(null)] // never ingested
    public async Task Stale_or_missing_coverage_in_one_municipality_gives_a_job(int? solnaDaysOld)
    {
        await AddCoverageAsync(Stockholm, daysOld: 1);
        if (solnaDaysOld is { } days)
        {
            await AddCoverageAsync(Solna, days);
        }

        var result = await Request([Stockholm, Solna]);

        Assert.NotNull(result.JobId);
        Assert.Equal(IngestJobStatus.Queued, result.Status);
        Assert.Equal([result.JobId.Value], _queue.Enqueued);

        await using var db = postgres.CreateContext();
        var job = await db.IngestJobs.SingleAsync();
        Assert.Equal(result.JobId, job.Id);
        Assert.Equal(Sni, job.Sni);
        Assert.Equal([Stockholm, Solna], job.Municipalities);
        Assert.Equal(Now, job.CreatedAt);
    }

    [Fact]
    public async Task Refresh_gives_a_job_even_when_coverage_is_fresh()
    {
        await AddCoverageAsync(Stockholm, daysOld: 0);

        var result = await Request([Stockholm], refresh: true);

        Assert.NotNull(result.JobId);
        Assert.Equal(IngestJobStatus.Queued, result.Status);
    }

    [Fact]
    public async Task Two_identical_requests_in_a_row_give_the_same_job()
    {
        var first = await Request([Stockholm, Solna]);
        // Another order and a repeat: still the same search.
        var second = await Request([Solna, Stockholm, Solna]);

        Assert.NotNull(first.JobId);
        Assert.Equal(first.JobId, second.JobId);
        Assert.Single(_queue.Enqueued);
    }

    [Fact]
    public async Task A_running_job_for_more_municipalities_is_reused()
    {
        var running = await AddJobAsync(IngestJobStatus.Running, Stockholm, Solna);

        var result = await Request([Solna]);

        Assert.Equal(new IngestRequestResult(running, IngestJobStatus.Running), result);
        Assert.Empty(_queue.Enqueued);
    }

    [Theory]
    [InlineData(IngestJobStatus.Succeeded)]
    [InlineData(IngestJobStatus.Failed)]
    public async Task A_finished_job_is_not_reused(string status)
    {
        var finished = await AddJobAsync(status, Stockholm);

        var result = await Request([Stockholm]);

        Assert.NotNull(result.JobId);
        Assert.NotEqual(finished, result.JobId);
    }

    [Fact]
    public async Task A_job_for_fewer_municipalities_is_not_reused()
    {
        var stockholmOnly = await AddJobAsync(IngestJobStatus.Queued, Stockholm);

        var result = await Request([Stockholm, Solna]);

        Assert.NotNull(result.JobId);
        Assert.NotEqual(stockholmOnly, result.JobId);
    }

    [Fact]
    public async Task No_municipalities_is_an_error()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Request([]));
    }

    private async Task<IngestRequestResult> Request(string[] municipalities, bool refresh = false)
    {
        await using var db = postgres.CreateContext();
        // Fully qualified: inside Reacher.Ingestion, "Options" is our own options namespace.
        var coordinator = new IngestCoordinator(
            db,
            _queue,
            Microsoft.Extensions.Options.Options.Create(new IngestOptions { FreshnessDays = 30 }),
            new FixedTimeProvider(Now));
        return await coordinator.RequestAsync(
            Sni, municipalities, refresh, CancellationToken.None);
    }

    private async Task AddCoverageAsync(string municipality, int daysOld)
    {
        await using var db = postgres.CreateContext();
        db.Coverage.Add(new Coverage
        {
            Sni = Sni,
            Municipality = municipality,
            FetchedAt = Now.AddDays(-daysOld),
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AddJobAsync(string status, params string[] municipalities)
    {
        await using var db = postgres.CreateContext();
        var job = new IngestJob
        {
            Id = Guid.NewGuid(),
            Sni = Sni,
            Municipalities = [.. municipalities],
            Status = status,
            CreatedAt = Now.AddMinutes(-5),
        };
        db.IngestJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>Records what the coordinator enqueues. Nothing reads it.</summary>
    private sealed class EnqueueRecorder : IIngestQueue
    {
        public List<Guid> Enqueued { get; } = [];

        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
        {
            Enqueued.Add(jobId);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("The coordinator only writes.");
    }
}
